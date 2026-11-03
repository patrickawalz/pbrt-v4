// (not in pbrt-v4) displacedmesh.cs
//
// Shape "displacedmesh": a triangle mesh with a displacement map that is ray traced as displaced geometry, without tessellation and
// without rebuilding any acceleration structure when the map changes. Each base triangle becomes a prism (see shellprism.cs) that is a
// primitive of the BVH, and a ray that enters the prism is traced through the displacement map in texture space.
//
// Exact traversal (Ogaki, "Nonlinear Ray Tracing for Displacement and Shell Mapping", SIGGRAPH Asia 2023): the straight ray is a
// rational function of the height h inside the prism, in texture space too. A min-max quadtree over the displacement map is traversed
// front to back; the interval of h in which the ray is inside the box of a node comes from quadratic inequalities, and a leaf (a cell
// of the map, two micro triangles over the corners of the cell) is intersected by solving a cubic in h. The traversal never steps, so
// it cannot skip a thin feature, and neighbouring prisms share their side faces (bilinear patches through the shared vertex normals),
// so the surface is watertight.
//
// Beyond the paper:
//  * Every prism has the range of heights of its own part of the map (the cells under its uv triangle), not the range of the whole map,
//    so the prisms are tight. The side faces stay shared between neighbours because they are the ruled surfaces through the shared
//    displacement directions, whatever heights the two prisms span (see DisplacedMesh).
//  * The quadtree nodes can be bounded with rectangular min-max queries (Thonat et al., "RMIP", SIGGRAPH Asia 2023; "rectangles"): the
//    part of the ray inside a node is projected to texture space, the heights under its bounding rectangle limit the heights of the
//    ray, and the two steps alternate. The leaves stay exact, so the surface is the same as without.
//  * Hard edges (vertices at one position with different normals) are welded for the displacement so that the prisms stay watertight,
//    with a miter factor that keeps the displacement distance (Hoetzlein 2025, Sec. 5.4, fixes the singularity of his parallel offset
//    prisms at creases by inserting triangles; Ogaki's prisms have none, but they would open cracks at a hard edge).
//  * The map can be stored compactly, and nodes in which the heights are a plane are intersected without descending (shellheights.cs).
//  * The displacement can be a procedural function of (u, v), bounded with interval arithmetic (shellexpression.cs).
//
// Parameters (as for "trianglemesh"; "plymesh" is read with "string filename"):
//   "point3 P", "integer indices", "normal N" (optional; area weighted vertex normals otherwise), "point2 uv" (required),
//   "texture displacement" (any float texture, evaluated at the uv of the grid points) or "string function" (an expression of u and v,
//   see shellexpression.cs), "float scale" (1), "float offset" (0),
//   "integer resolution" (the grid is resolution x resolution points, default 1024, or 4096 for a function), "bool smoothnormals"
//   (true: the shading normal keeps the smoothness of the interpolated vertex normals, Hoetzlein 2025, Eq. 10),
//   "string storage" ("float", "fixed16" or "packed": how the grid of heights is stored), "float flatness" (the deviation, in
//   normalized heights, up to which a node of the pyramid is intersected as two triangles over its corners; 0: only exact planes,
//   negative: never), "bool tightranges" (true: a height range per prism), "bool rectangles" (false: RMIP bounds, which visit fewer nodes but cost more per node),
//   "integer rectanglelevel" (2) and "integer rectanglepasses" (2): the smallest level of the quadtree that is bounded with
//   rectangles, and the rounds of the projection per node,
//   "string creases" ("weld" or "none"), "float miterlimit" (3: the largest factor on the displacement at a weld).
// The displacement is along the vertex normals in object space, by scale * texture + offset. The uv of the base triangles should lie
// within one tile of the map (tiles are repeated by the integer part of the uv of the triangle).

using System;
using System.Collections.Generic;

namespace pbrt
{

    // ShellWorkspace Definition: the scratch memory of one thread
    internal sealed class ShellWorkspace
    {
        public readonly ShellPrism prism = new ShellPrism(new SdfVector[3], new SdfVector[3]);
        public readonly ShellRay ray = new ShellRay();
        public readonly double[] gx = new double[3];
        public readonly double[] gy = new double[3];
        public readonly double[] distances = new double[10];
        public readonly double[] heights = new double[10];
        public readonly double[] roots = new double[3];
        public readonly double[] quadraticRoots = new double[2];
        public readonly double[] boundsRoots = new double[2];
        public readonly double[] inverseHeights = new double[3];
        public readonly double[] inverseAlphas = new double[3];
        public readonly double[] inverseBetas = new double[3];
        public readonly int[] stackLevel = new int[256];
        public readonly int[] stackI = new int[256];
        public readonly int[] stackJ = new int[256];
        public readonly double[] stackT = new double[256];
        public readonly double[] stackLow = new double[256];
        public readonly double[] stackHigh = new double[256];
        public readonly int[] childI = new int[4];
        public readonly int[] childJ = new int[4];
        public readonly double[] childT = new double[4];
        public readonly double[] childLow = new double[4];
        public readonly double[] childHigh = new double[4];

        // The map of the heights of the triangle to the heights of the prism: height = heightBase + heightScale * h
        public double heightBase, heightScale, inverseHeightScale;
        public bool useRectangles;
        public int rectangleMinimumLevel, rectanglePasses;
        public ShellStatistics statistics = new ShellStatistics();
    }

    // ShellStatistics Definition: what the traversal did on one thread
    public sealed class ShellStatistics
    {
        public long traces, nodes, leaves;
    }

    // ShellHit Definition
    internal struct ShellHit
    {
        public double t, h, alpha, beta;
        public int cellX, cellY, cellSize, triangle;
        public double tileU, tileV;
    }

    // DisplacedMeshOptions Definition
    public sealed class DisplacedMeshOptions
    {
        public bool smoothNormals = true;
        public bool tightRanges = true;
        public bool rectangleBounds = false;
        // The smallest level of the quadtree whose nodes are bounded with rectangles, and the number of rounds of projecting the ray to
        // texture space and clipping its heights
        public int rectangleMinimumLevel = 2;
        public int rectanglePasses = 2;
        public bool weldCreases = true;
        public double miterLimit = 3;
    }

    // DisplacedMesh Definition: the prisms of the triangles of the mesh. Vertex v has the displacement direction D(v) (the vertex normal,
    // or the welded direction at a hard edge) and a point of the displaced surface is P + d D with d the distance in object space. The
    // prism of a triangle spans the distances d0 + heightBase * thickness to d0 + (heightBase + heightScale) * thickness. Its side
    // faces are the ruled surfaces (1 - s) (P0 + d D0) + s (P1 + d D1) for those distances, so two triangles that share an edge share
    // the surface of the side whatever their ranges are: the prisms are watertight.
    public sealed class DisplacedMesh
    {
        // DisplacedMesh Public Methods
        public DisplacedMesh(Transform renderFromObject, bool reverseOrientation, int[] indices, Point3f[] p, Normal3f[] n, Point2f[] uv,
                             ShellHeights field, double minimumDisplacement, double maximumDisplacement, DisplacedMeshOptions options)
        {
            this.indices = indices;
            this.uv = uv;
            this.field = field;
            this.reverseOrientation = reverseOrientation;
            smoothNormals = options.smoothNormals;
            rectangleBounds = options.rectangleBounds;
            rectangleMinimumLevel = options.rectangleMinimumLevel;
            rectanglePasses = options.rectanglePasses;
            int vertexCount = p.Length;
            renderP = new SdfVector[vertexCount];
            shadingN = new SdfVector[vertexCount];
            SdfVector[] directions = options.weldCreases ? WeldedDirections(p, n, indices, options.miterLimit) : null;
            SdfVector[] renderDirection = new SdfVector[vertexCount];
            for (int i = 0; i < vertexCount; ++i)
            {
                Normal3f unit = pbrt.Normalize(n[i]);
                Point3f renderPoint = renderFromObject.Apply(p[i]);
                renderP[i] = new SdfVector(renderPoint.x, renderPoint.y, renderPoint.z);
                Vector3f direction = directions != null ? new Vector3f((Float)directions[i].x, (Float)directions[i].y, (Float)directions[i].z)
                                                        : new Vector3f(unit.x, unit.y, unit.z);
                Vector3f renderVector = renderFromObject.Apply(direction);
                renderDirection[i] = new SdfVector(renderVector.x, renderVector.y, renderVector.z);
                Normal3f renderNormal = pbrt.Normalize(renderFromObject.Apply(unit));
                shadingN[i] = new SdfVector(renderNormal.x, renderNormal.y, renderNormal.z);
            }
            int triangleCount = indices.Length / 3;
            triangleBase = new SdfVector[3 * triangleCount];
            triangleOffset = new SdfVector[3 * triangleCount];
            heightBase = new double[triangleCount];
            heightScale = new double[triangleCount];
            triangleEmpty = new bool[triangleCount];
            gridCorners = new double[6 * triangleCount];
            tileU = new double[triangleCount];
            tileV = new double[triangleCount];
            double thickness = maximumDisplacement - minimumDisplacement;
            const double minimumExtent = 1e-4;
            for (int t = 0; t < triangleCount; ++t)
            {
                // The uv triangle in grid units of the tile that holds its centroid
                Point2f t0 = uv[indices[3 * t]], t1 = uv[indices[3 * t + 1]], t2 = uv[indices[3 * t + 2]];
                tileU[t] = Math.Floor(((double)t0.x + t1.x + t2.x) / 3);
                tileV[t] = Math.Floor(((double)t0.y + t1.y + t2.y) / 3);
                gridCorners[6 * t] = (t0.x - tileU[t]) * field.cellsX;
                gridCorners[6 * t + 1] = (t0.y - tileV[t]) * field.cellsY;
                gridCorners[6 * t + 2] = (t1.x - tileU[t]) * field.cellsX;
                gridCorners[6 * t + 3] = (t1.y - tileV[t]) * field.cellsY;
                gridCorners[6 * t + 4] = (t2.x - tileU[t]) * field.cellsX;
                gridCorners[6 * t + 5] = (t2.y - tileV[t]) * field.cellsY;
                // A little more than the range in either case: the surface often touches the lowest and the highest height of the whole
                // map exactly (a flat floor), and a hit on the face of the prism must be inside it
                double low = -2e-6, high = 1 + 2e-6;
                if (options.tightRanges)
                {
                    if (field.TriangleRange(gridCorners[6 * t], gridCorners[6 * t + 1], gridCorners[6 * t + 2], gridCorners[6 * t + 3],
                                            gridCorners[6 * t + 4], gridCorners[6 * t + 5], out double rangeLow, out double rangeHigh))
                    {
                        // A little more than the range, and never thinner than a minimum, so that the prism is not degenerate
                        low = rangeLow - 2e-6;
                        high = rangeHigh + 2e-6;
                        if (high - low < minimumExtent)
                        {
                            double middle = 0.5 * (low + high);
                            low = middle - 0.5 * minimumExtent;
                            high = middle + 0.5 * minimumExtent;
                        }
                    }
                    else
                    {
                        triangleEmpty[t] = true;
                    }
                }
                heightBase[t] = low;
                heightScale[t] = high - low;
                for (int k = 0; k < 3; ++k)
                {
                    int v = indices[3 * t + k];
                    triangleBase[3 * t + k] = renderP[v] + renderDirection[v] * (minimumDisplacement + low * thickness);
                    triangleOffset[3 * t + k] = renderDirection[v] * ((high - low) * thickness);
                }
            }
        }

        public int TriangleCount()
        {
            return indices.Length / 3;
        }

        // DisplacedMesh Public Members
        public readonly int[] indices;
        public readonly SdfVector[] renderP, shadingN, triangleBase, triangleOffset;
        public readonly double[] heightBase, heightScale;
        // The corners (x0, y0, x1, y1, x2, y2) of the uv triangle of every triangle in grid units of its tile, and the tile
        public readonly double[] gridCorners, tileU, tileV;
        public readonly bool[] triangleEmpty;
        public readonly Point2f[] uv;
        public readonly ShellHeights field;
        public readonly bool reverseOrientation, smoothNormals, rectangleBounds;
        public readonly int rectangleMinimumLevel, rectanglePasses;

        // DisplacedMesh Private Methods

        // The unit normal of a triangle, on the side that the normals of its vertices point to (the winding of a mesh need not agree with
        // them); zero for a degenerate triangle
        private static SdfVector OrientedFaceNormal(SdfVector a, SdfVector b, SdfVector c, SdfVector vertexNormalSum)
        {
            SdfVector faceNormal = SdfVector.Cross(b - a, c - a);
            if (faceNormal.Length() == 0)
            {
                return faceNormal;
            }
            faceNormal = faceNormal.Normalized();
            return SdfVector.Dot(faceNormal, vertexNormalSum) < 0 ? -faceNormal : faceNormal;
        }

        // The displacement directions: the vertex normal, except at a position where vertices have different normals (a hard edge or
        // corner). There the direction is the same for all the vertices at the position, the average of the normals of the faces there
        // weighted by their angles at it, and it is scaled by the miter factor 1 / (smallest cosine to a face normal), so that the
        // distance to each face's plane is at least the displacement distance (limited by miterLimit).
        private static SdfVector[] WeldedDirections(Point3f[] p, Normal3f[] n, int[] indices, double miterLimit)
        {
            int vertexCount = p.Length;
            Bounds3f box = new Bounds3f(p[0]);
            for (int i = 1; i < vertexCount; ++i)
            {
                box = pbrt.Union(box, p[i]);
            }
            double cell = 1e-6 * Math.Max((double)pbrt.Length(box.pMax - box.pMin), 1e-12);
            Dictionary<(long, long, long), int> groupOf = new Dictionary<(long, long, long), int>();
            int[] group = new int[vertexCount];
            List<int> first = new List<int>();
            for (int i = 0; i < vertexCount; ++i)
            {
                (long, long, long) key = ((long)Math.Round(p[i].x / cell), (long)Math.Round(p[i].y / cell), (long)Math.Round(p[i].z / cell));
                if (!groupOf.TryGetValue(key, out int id))
                {
                    id = first.Count;
                    groupOf.Add(key, id);
                    first.Add(i);
                }
                group[i] = id;
            }
            SdfVector[] unit = new SdfVector[vertexCount];
            for (int i = 0; i < vertexCount; ++i)
            {
                Normal3f normal = pbrt.Normalize(n[i]);
                unit[i] = new SdfVector(normal.x, normal.y, normal.z);
            }
            bool[] crease = new bool[first.Count];
            for (int i = 0; i < vertexCount; ++i)
            {
                if (SdfVector.Dot(unit[i], unit[first[group[i]]]) < 1 - 1e-6)
                {
                    crease[group[i]] = true;
                }
            }
            SdfVector[] sum = new SdfVector[first.Count];
            for (int t = 0; t < indices.Length / 3; ++t)
            {
                SdfVector[] corner = new SdfVector[3];
                for (int k = 0; k < 3; ++k)
                {
                    corner[k] = new SdfVector(p[indices[3 * t + k]].x, p[indices[3 * t + k]].y, p[indices[3 * t + k]].z);
                }
                SdfVector faceNormal = OrientedFaceNormal(corner[0], corner[1], corner[2], unit[indices[3 * t]] + unit[indices[3 * t + 1]] +
                                                                                                   unit[indices[3 * t + 2]]);
                if (faceNormal.Length() == 0)
                {
                    continue;
                }
                for (int k = 0; k < 3; ++k)
                {
                    int g = group[indices[3 * t + k]];
                    if (!crease[g])
                    {
                        continue;
                    }
                    SdfVector e0 = corner[(k + 1) % 3] - corner[k], e1 = corner[(k + 2) % 3] - corner[k];
                    double denominator = e0.Length() * e1.Length();
                    double angle = denominator > 0 ? Math.Acos(Math.Min(Math.Max(SdfVector.Dot(e0, e1) / denominator, -1), 1)) : 0;
                    sum[g] = sum[g] + faceNormal * angle;
                }
            }
            SdfVector[] welded = new SdfVector[first.Count];
            double[] smallestCosine = new double[first.Count];
            for (int g = 0; g < first.Count; ++g)
            {
                welded[g] = crease[g] && sum[g].Length() > 0 ? sum[g].Normalized() : unit[first[g]];
                smallestCosine[g] = 1;
            }
            for (int t = 0; t < indices.Length / 3; ++t)
            {
                SdfVector a = new SdfVector(p[indices[3 * t]].x, p[indices[3 * t]].y, p[indices[3 * t]].z);
                SdfVector b = new SdfVector(p[indices[3 * t + 1]].x, p[indices[3 * t + 1]].y, p[indices[3 * t + 1]].z);
                SdfVector c = new SdfVector(p[indices[3 * t + 2]].x, p[indices[3 * t + 2]].y, p[indices[3 * t + 2]].z);
                SdfVector faceNormal = OrientedFaceNormal(a, b, c, unit[indices[3 * t]] + unit[indices[3 * t + 1]] + unit[indices[3 * t + 2]]);
                if (faceNormal.Length() == 0)
                {
                    continue;
                }
                for (int k = 0; k < 3; ++k)
                {
                    int g = group[indices[3 * t + k]];
                    if (crease[g])
                    {
                        smallestCosine[g] = Math.Min(smallestCosine[g], SdfVector.Dot(welded[g], faceNormal));
                    }
                }
            }
            SdfVector[] directions = new SdfVector[vertexCount];
            for (int i = 0; i < vertexCount; ++i)
            {
                int g = group[i];
                if (!crease[g])
                {
                    directions[i] = unit[i];
                    continue;
                }
                double factor = smallestCosine[g] > 1 / miterLimit ? 1 / smallestCosine[g] : miterLimit;
                directions[i] = welded[g] * factor;
            }
            return directions;
        }
    }

    // DisplacedTriangle Definition
    public sealed unsafe class DisplacedTriangle : Shape
    {
        // DisplacedTriangle Public Methods
        public DisplacedTriangle(DisplacedMesh mesh, int triIndex)
        {
            this.mesh = mesh;
            this.triIndex = triIndex;
        }

        public override Bounds3f Bounds()
        {
            Bounds3f box = new Bounds3f();
            bool first = true;
            for (int k = 0; k < 3; ++k)
            {
                for (int level = 0; level < 2; ++level)
                {
                    SdfVector q = mesh.triangleBase[3 * triIndex + k] + mesh.triangleOffset[3 * triIndex + k] * level;
                    Point3f point = new Point3f((Float)q.x, (Float)q.y, (Float)q.z);
                    box = first ? new Bounds3f(point) : pbrt.Union(box, point);
                    first = false;
                }
            }
            // Round the box outwards so that the single precision box contains the double precision prism
            float extent = Math.Max(Math.Max(Math.Abs(box.pMin.x), Math.Abs(box.pMax.x)),
                                    Math.Max(Math.Max(Math.Abs(box.pMin.y), Math.Abs(box.pMax.y)),
                                             Math.Max(Math.Abs(box.pMin.z), Math.Abs(box.pMax.z))));
            float amount = 1e-5f * Math.Max(1f, extent);
            Vector3f pad = new Vector3f(amount, amount, amount);
            return new Bounds3f(box.pMin - pad, box.pMax + pad);
        }

        public override DirectionCone NormalBounds()
        {
            return DirectionCone.EntireSphere();
        }

        public override pstd.optional<ShapeIntersection> Intersect(Ray ray, Float tMax = Infinity)
        {
            if (!Trace(ray, tMax, false, out ShellHit hit, out double dirLength))
            {
                return default;
            }
            SurfaceInteraction intr = InteractionFromHit(ray, hit);
            return new ShapeIntersection(intr, (Float)(hit.t / dirLength));
        }

        public override bool IntersectP(Ray ray, Float tMax = Infinity)
        {
            return Trace(ray, tMax, true, out ShellHit hit, out double dirLength);
        }

        // The area of the base triangle (the shape does not support sampling, so it is used for nothing but the scale of lights)
        public override Float Area()
        {
            int i0 = mesh.indices[3 * triIndex], i1 = mesh.indices[3 * triIndex + 1], i2 = mesh.indices[3 * triIndex + 2];
            return (Float)(0.5 * SdfVector.Cross(mesh.renderP[i1] - mesh.renderP[i0], mesh.renderP[i2] - mesh.renderP[i0]).Length());
        }

        public override pstd.optional<ShapeSample> Sample(Point2f u)
        {
            pbrt.ErrorExit("displacedmesh: area lights on a displaced mesh are not supported yet.");
            return default;
        }

        public override Float PDF(Interaction intr)
        {
            pbrt.ErrorExit("displacedmesh: area lights on a displaced mesh are not supported yet.");
            return 0;
        }

        public override pstd.optional<ShapeSample> Sample(ShapeSampleContext ctx, Point2f u)
        {
            pbrt.ErrorExit("displacedmesh: area lights on a displaced mesh are not supported yet.");
            return default;
        }

        public override Float PDF(ShapeSampleContext ctx, Vector3f wi)
        {
            pbrt.ErrorExit("displacedmesh: area lights on a displaced mesh are not supported yet.");
            return 0;
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ DisplacedTriangle triIndex: %d ]", triIndex);
        }

        // What the traversal did on the calling thread since the last reset
        public static ShellStatistics ThreadStatistics()
        {
            return Workspace().statistics;
        }

        public static void ResetThreadStatistics()
        {
            Workspace().statistics = new ShellStatistics();
        }

        // Creates the triangles of the mesh from the parameters
        public static pstd.vector<Shape> Create(Transform renderFromObject, bool reverseOrientation, ParameterDictionary parameters,
                                                IDictionary<string, FloatTexture> floatTextures, FileLoc loc)
        {
            string filename = parameters.GetOneString("filename", "");
            MeshData(filename, parameters.GetIntArray("indices"), parameters.GetPoint3fArray("P"), parameters.GetNormal3fArray("N"),
                     parameters.GetPoint2fArray("uv"), loc, out int[] indices, out Point3f[] p, out Normal3f[] n, out Point2f[] uvArray);
            double scale = parameters.GetOneFloat("scale", 1), offsetValue = parameters.GetOneFloat("offset", 0);
            string function = parameters.GetOneString("function", "");
            string textureName = parameters.GetTexture("displacement");
            ShellHeights field;
            double low, high;
            if (function.Length != 0)
            {
                int resolution = Math.Max(2, parameters.GetOneInt("resolution", 4096));
                int depth = 1;
                while ((1 << depth) < resolution - 1 && depth < 24)
                {
                    ++depth;
                }
                try
                {
                    ShellProceduralField procedural = new ShellProceduralField(ShellExpression.Parse(function), depth, scale, offsetValue);
                    field = procedural;
                    low = procedural.Low();
                    high = procedural.High();
                }
                catch (FormatException e)
                {
                    pbrt.ErrorExit(loc, "displacedmesh: \"string function\": " + e.Message);
                    return null;
                }
            }
            else
            {
                if (textureName.Length == 0 || !floatTextures.TryGetValue(textureName, out FloatTexture texture))
                {
                    pbrt.ErrorExit(loc, "displacedmesh: \"texture displacement\" must name a float texture (or give a \"string function\").");
                    return null;
                }
                int resolution = Math.Max(2, parameters.GetOneInt("resolution", 1024));
                float[] samples = new float[resolution * resolution];
                pbrt.ParallelFor(0, resolution, (int row) =>
                {
                    for (int column = 0; column < resolution; ++column)
                    {
                        TextureEvalContext ctx = default;
                        ctx.p = new Point3f(0, 0, 0);
                        ctx.uv = new Point2f((Float)column / (resolution - 1), (Float)row / (resolution - 1));
                        samples[row * resolution + column] = (float)(scale * new UniversalTextureEvaluator().Apply(texture, ctx) + offsetValue);
                    }
                });
                low = double.MaxValue;
                high = double.MinValue;
                for (int i = 0; i < samples.Length; ++i)
                {
                    low = Math.Min(low, samples[i]);
                    high = Math.Max(high, samples[i]);
                }
                double thickness = high - low;
                if (thickness < 1e-9)
                {
                    // A constant map: a very thin shell, so that the prism is not degenerate
                    thickness = 1e-6;
                    high = low + thickness;
                }
                for (int i = 0; i < samples.Length; ++i)
                {
                    samples[i] = (float)((samples[i] - low) / thickness);
                }
                string storageName = parameters.GetOneString("storage", "float");
                ShellStorage storage = storageName == "fixed16" ? ShellStorage.Fixed16 : storageName == "packed" ? ShellStorage.Packed : ShellStorage.Float32;
                if (storageName != "float" && storageName != "fixed16" && storageName != "packed")
                {
                    pbrt.ErrorExit(loc, "displacedmesh: \"storage\" must be \"float\", \"fixed16\" or \"packed\".");
                }
                field = new ShellHeightField(resolution, resolution, samples, storage, parameters.GetOneFloat("flatness", 0));
            }
            DisplacedMeshOptions options = new DisplacedMeshOptions();
            options.smoothNormals = parameters.GetOneBool("smoothnormals", true);
            options.tightRanges = parameters.GetOneBool("tightranges", true);
            options.rectangleBounds = parameters.GetOneBool("rectangles", false);
            options.rectangleMinimumLevel = Math.Max(1, parameters.GetOneInt("rectanglelevel", 2));
            options.rectanglePasses = Math.Max(1, parameters.GetOneInt("rectanglepasses", 2));
            string creases = parameters.GetOneString("creases", "weld");
            if (creases != "weld" && creases != "none")
            {
                pbrt.ErrorExit(loc, "displacedmesh: \"creases\" must be \"weld\" or \"none\".");
            }
            options.weldCreases = creases == "weld";
            options.miterLimit = Math.Max(1, parameters.GetOneFloat("miterlimit", 3));
            DisplacedMesh mesh = new DisplacedMesh(renderFromObject, reverseOrientation, indices, p, n, uvArray, field, low, high, options);
            pstd.vector<Shape> shapes = new pstd.vector<Shape>();
            for (int i = 0; i < mesh.TriangleCount(); ++i)
            {
                shapes.push_back(new DisplacedTriangle(mesh, i));
            }
            return shapes;
        }

        // DisplacedTriangle Private Methods

        // The triangle mesh of a shape or texture: from a PLY file or from the parameters; normals are computed when they are missing
        public static void MeshData(string filename, List<int> indexList, List<Point3f> pointList, List<Normal3f> normalList,
                                    List<Point2f> uvList, FileLoc loc, out int[] indices, out Point3f[] p, out Normal3f[] n,
                                    out Point2f[] uv)
        {
            if (filename.Length != 0)
            {
                TriQuadMesh ply = TriQuadMesh.ReadPLY(pbrt.ResolveFilename(filename));
                ply.ConvertToOnlyTriangles();
                indexList = new List<int>(ply.triIndices.ToArray());
                pointList = new List<Point3f>(ply.p.ToArray());
                normalList = new List<Normal3f>(ply.n.ToArray());
                uvList = new List<Point2f>(ply.uv.ToArray());
            }
            if (pointList.Count == 0 || indexList.Count == 0 || indexList.Count % 3 != 0)
            {
                pbrt.ErrorExit(loc, "displaced mesh: \"P\" and \"indices\" (a multiple of three) are required.");
            }
            if (uvList.Count != pointList.Count)
            {
                pbrt.ErrorExit(loc, "displaced mesh: \"uv\" is required, one per vertex.");
            }
            p = pointList.ToArray();
            indices = indexList.ToArray();
            n = normalList.Count == p.Length ? normalList.ToArray() : VertexNormals(p, indices);
            uv = uvList.ToArray();
        }

        private static Normal3f[] VertexNormals(Point3f[] p, int[] indices)
        {
            Vector3f[] sum = new Vector3f[p.Length];
            for (int t = 0; t < indices.Length / 3; ++t)
            {
                int i0 = indices[3 * t], i1 = indices[3 * t + 1], i2 = indices[3 * t + 2];
                Vector3f faceNormal = pbrt.Cross(p[i1] - p[i0], p[i2] - p[i0]);
                sum[i0] += faceNormal;
                sum[i1] += faceNormal;
                sum[i2] += faceNormal;
            }
            Normal3f[] n = new Normal3f[p.Length];
            for (int i = 0; i < p.Length; ++i)
            {
                Vector3f unit = pbrt.Normalize(sum[i]);
                n[i] = new Normal3f(unit.x, unit.y, unit.z);
            }
            return n;
        }

        [ThreadStatic]
        private static ShellWorkspace workspace;

        private static ShellWorkspace Workspace()
        {
            if (workspace == null)
            {
                workspace = new ShellWorkspace();
            }
            return workspace;
        }

        // Narrows [low, high] to the smallest interval that contains every h in it with c0 + c1 h + c2 h^2 >= 0; false if there is none
        private static bool Hull(double c0, double c1, double c2, ref double low, ref double high, double[] roots)
        {
            int count = ShellPolynomial.QuadraticRoots(c0, c1, c2, low, high, roots);
            double newLow = double.MaxValue, newHigh = double.MinValue;
            double a = low;
            for (int k = 0; k <= count; ++k)
            {
                double b = k < count ? roots[k] : high;
                double middle = 0.5 * (a + b);
                if ((c2 * middle + c1) * middle + c0 >= 0 || (c2 * a + c1) * a + c0 >= 0 || (c2 * b + c1) * b + c0 >= 0)
                {
                    newLow = Math.Min(newLow, a);
                    newHigh = Math.Max(newHigh, b);
                }
                a = b;
            }
            if (newLow > newHigh)
            {
                return false;
            }
            low = newLow;
            high = newHigh;
            return true;
        }

        // Narrows [low, high] to the heights in which the ray is inside the box [x0, x1] x [y0, y1] of the grid (conservatively: the
        // hull of the heights inside)
        private static bool BoxInterval(ShellWorkspace ws, double x0, double x1, double y0, double y1, ref double low, ref double high)
        {
            double[] d = ws.ray.denominator;
            return Hull(ws.gx[0] - x0 * d[0], ws.gx[1] - x0 * d[1], ws.gx[2] - x0 * d[2], ref low, ref high, ws.quadraticRoots) &&
                   Hull(x1 * d[0] - ws.gx[0], x1 * d[1] - ws.gx[1], x1 * d[2] - ws.gx[2], ref low, ref high, ws.quadraticRoots) &&
                   Hull(ws.gy[0] - y0 * d[0], ws.gy[1] - y0 * d[1], ws.gy[2] - y0 * d[2], ref low, ref high, ws.quadraticRoots) &&
                   Hull(y1 * d[0] - ws.gy[0], y1 * d[1] - ws.gy[1], y1 * d[2] - ws.gy[2], ref low, ref high, ws.quadraticRoots);
        }

        // The extremes of g(h) / D(h) for h in [low, high]: at the ends and where the derivative (g' D - g D') / D^2 vanishes, which is
        // the root of a quadratic because the cubic terms cancel
        private static void Extremes(ShellWorkspace ws, double[] g, double low, double high, ref double minimum, ref double maximum)
        {
            double[] d = ws.ray.denominator;
            double c0 = g[1] * d[0] - g[0] * d[1], c1 = 2 * g[2] * d[0] - 2 * g[0] * d[2], c2 = g[2] * d[1] - g[1] * d[2];
            int count = ShellPolynomial.QuadraticRoots(c0, c1, c2, low, high, ws.boundsRoots);
            for (int k = 0; k < count; ++k)
            {
                double value = ShellRay.Polynomial(g, ws.boundsRoots[k]) / ShellRay.Polynomial(d, ws.boundsRoots[k]);
                minimum = Math.Min(minimum, value);
                maximum = Math.Max(maximum, value);
            }
        }

        // The interval of heights in which the ray is inside the box of a node of the quadtree (conservatively: the hull of the
        // heights inside), clipped to the range [lowest, highest] of the heights of the ray in the prism. With rectangular bounds
        // (RMIP) the part of the ray inside the box is projected to texture space; the heights of the map under the bounding rectangle
        // of the projection limit the heights of the ray, which shrinks the part inside the box, and so on.
        private bool NodeInterval(ShellWorkspace ws, int level, int i, int j, double lowest, double highest, out double low, out double high)
        {
            ++ws.statistics.nodes;
            ShellHeights field = mesh.field;
            low = lowest;
            high = highest;
            field.NodeRange(level, i, j, out double nodeLow, out double nodeHigh);
            const double slack = 1e-6;
            low = Math.Max(low, (nodeLow - slack - ws.heightBase) * ws.inverseHeightScale);
            high = Math.Min(high, (nodeHigh + slack - ws.heightBase) * ws.inverseHeightScale);
            if (low > high)
            {
                return false;
            }
            double x0 = (double)i * (1 << level), x1 = (double)(i + 1) * (1 << level);
            double y0 = (double)j * (1 << level), y1 = (double)(j + 1) * (1 << level);
            if (!BoxInterval(ws, x0, x1, y0, y1, ref low, ref high))
            {
                return false;
            }
            if (ws.useRectangles && level >= ws.rectangleMinimumLevel)
            {
                double[] d = ws.ray.denominator;
                for (int pass = 0; pass < ws.rectanglePasses; ++pass)
                {
                    double dLow = ShellRay.Polynomial(d, low), dHigh = ShellRay.Polynomial(d, high);
                    if (!(dLow > 0) || !(dHigh > 0))
                    {
                        break;
                    }
                    double xa = ShellRay.Polynomial(ws.gx, low) / dLow, xb = ShellRay.Polynomial(ws.gx, high) / dHigh;
                    double ya = ShellRay.Polynomial(ws.gy, low) / dLow, yb = ShellRay.Polynomial(ws.gy, high) / dHigh;
                    double rx0 = Math.Min(xa, xb), rx1 = Math.Max(xa, xb), ry0 = Math.Min(ya, yb), ry1 = Math.Max(ya, yb);
                    Extremes(ws, ws.gx, low, high, ref rx0, ref rx1);
                    Extremes(ws, ws.gy, low, high, ref ry0, ref ry1);
                    rx0 = Math.Max(rx0, x0);
                    rx1 = Math.Min(rx1, x1);
                    ry0 = Math.Max(ry0, y0);
                    ry1 = Math.Min(ry1, y1);
                    if (rx0 > rx1 + 1e-9 || ry0 > ry1 + 1e-9)
                    {
                        return false;
                    }
                    if (!field.RectangleRange(rx0, ry0, rx1, ry1, out double rangeLow, out double rangeHigh))
                    {
                        return false;
                    }
                    double newLow = Math.Max(low, (rangeLow - slack - ws.heightBase) * ws.inverseHeightScale);
                    double newHigh = Math.Min(high, (rangeHigh + slack - ws.heightBase) * ws.inverseHeightScale);
                    if (newLow > newHigh)
                    {
                        return false;
                    }
                    bool shrunk = newHigh - newLow < 0.8 * (high - low);
                    low = newLow;
                    high = newHigh;
                    if (!shrunk)
                    {
                        break;
                    }
                    if (!BoxInterval(ws, x0, x1, y0, y1, ref low, ref high))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        // The vertices of a micro triangle over the square of the grid with the corner (i, j) and the given size, in (grid x, grid y,
        // height)
        private void MicroTriangle(int i, int j, int size, int triangle, out double ax, out double ay, out double ah, out double bx,
                                   out double by, out double bh, out double cx, out double cy, out double ch)
        {
            ShellHeights field = mesh.field;
            double h00 = field.Sample(i, j), h10 = field.Sample(i + size, j), h01 = field.Sample(i, j + size);
            double h11 = field.Sample(i + size, j + size);
            ax = i;
            ay = j;
            ah = h00;
            if (triangle == 0)
            {
                bx = i + size;
                by = j;
                bh = h10;
                cx = i + size;
                cy = j + size;
                ch = h11;
            }
            else
            {
                bx = i + size;
                by = j + size;
                bh = h11;
                cx = i;
                cy = j + size;
                ch = h01;
            }
        }

        // Intersects the two micro triangles of a leaf (a cell, or a flat node) with the ray; updates the hit when one is closer than
        // tBest
        private bool IntersectCell(ShellWorkspace ws, SdfVector o, SdfVector w, int i, int j, int size, double low, double high,
                                   double tileU, double tileV, ref double tBest, ref ShellHit best)
        {
            ++ws.statistics.leaves;
            bool found = false;
            double[] d = ws.ray.denominator;
            for (int triangle = 0; triangle < 2; ++triangle)
            {
                MicroTriangle(i, j, size, triangle, out double ax, out double ay, out double ah, out double bx, out double by, out double bh,
                              out double cx, out double cy, out double ch);
                // The plane of the micro triangle: nx x + ny y + nh H + k = 0 with the height H = heightBase + heightScale h of the map
                double ux = bx - ax, uy = by - ay, uh = bh - ah, vx = cx - ax, vy = cy - ay, vh = ch - ah;
                double nx = uy * vh - uh * vy, ny = uh * vx - ux * vh, nHeight = ux * vy - uy * vx;
                double k0 = -(nx * ax + ny * ay + nHeight * ah);
                double nh = nHeight * ws.heightScale, k = k0 + nHeight * ws.heightBase;
                // nx X(h) + ny Y(h) + (nh h + k) D(h) = 0
                double c0 = nx * ws.gx[0] + ny * ws.gy[0] + k * d[0];
                double c1 = nx * ws.gx[1] + ny * ws.gy[1] + k * d[1] + nh * d[0];
                double c2 = nx * ws.gx[2] + ny * ws.gy[2] + k * d[2] + nh * d[1];
                double c3 = nh * d[2];
                int count = ShellPolynomial.CubicRoots(c0, c1, c2, c3, low - 1e-12, high + 1e-12, ws.roots);
                for (int r = 0; r < count; ++r)
                {
                    double h = ws.roots[r];
                    double denominator = ShellRay.Polynomial(d, h);
                    double gx = ShellRay.Polynomial(ws.gx, h) / denominator, gy = ShellRay.Polynomial(ws.gy, h) / denominator;
                    double fx = (gx - i) / size, fy = (gy - j) / size;
                    const double tolerance = 1e-9;
                    bool inside = triangle == 0 ? (fx >= fy - tolerance && fx <= 1 + tolerance && fy >= -tolerance)
                                                : (fy >= fx - tolerance && fy <= 1 + tolerance && fx >= -tolerance);
                    if (!inside)
                    {
                        continue;
                    }
                    double alpha = ShellRay.Polynomial(ws.ray.alpha, h) / denominator, beta = ShellRay.Polynomial(ws.ray.beta, h) / denominator;
                    if (alpha < -tolerance || beta < -tolerance || alpha + beta > 1 + tolerance)
                    {
                        continue;
                    }
                    double t = SdfVector.Dot(w, ws.prism.Point(alpha, beta, h) - o);
                    if (!(t > 0) || t >= tBest)
                    {
                        continue;
                    }
                    tBest = t;
                    best.t = t;
                    best.h = h;
                    best.alpha = alpha;
                    best.beta = beta;
                    best.cellX = i;
                    best.cellY = j;
                    best.cellSize = size;
                    best.triangle = triangle;
                    best.tileU = tileU;
                    best.tileV = tileV;
                    found = true;
                }
            }
            return found;
        }

        // Finds the closest hit of the ray with the displaced surface in this prism (t in units of the unit direction)
        private bool Trace(Ray ray, double tMaxRay, bool anyHit, out ShellHit best, out double dirLength)
        {
            best = default;
            SdfVector o = new SdfVector(ray.o.x, ray.o.y, ray.o.z);
            SdfVector direction = new SdfVector(ray.d.x, ray.d.y, ray.d.z);
            dirLength = direction.Length();
            if (dirLength == 0 || mesh.triangleEmpty[triIndex])
            {
                return false;
            }
            SdfVector w = direction / dirLength;
            double tMax = double.IsInfinity(tMaxRay) ? double.MaxValue : tMaxRay * dirLength;
            ShellWorkspace ws = Workspace();
            ++ws.statistics.traces;
            int v0 = mesh.indices[3 * triIndex], v1 = mesh.indices[3 * triIndex + 1], v2 = mesh.indices[3 * triIndex + 2];
            ShellPrism prism = ws.prism;
            SetPrism(prism);
            ws.heightBase = mesh.heightBase[triIndex];
            ws.heightScale = mesh.heightScale[triIndex];
            ws.inverseHeightScale = 1 / ws.heightScale;
            ws.useRectangles = mesh.rectangleBounds;
            ws.rectangleMinimumLevel = mesh.rectangleMinimumLevel;
            ws.rectanglePasses = mesh.rectanglePasses;

            // Where the ray is inside the prism
            int crossings = prism.FaceCrossings(o, w, tMax, ws.distances, ws.heights, ws.quadraticRoots);
            double tIn, hIn, tOut, hOut;
            int found = prism.Inverse(o, -1e-9, 1 + 1e-9, ws.inverseHeights, ws.inverseAlphas, ws.inverseBetas);
            int inside = -1;
            for (int k = 0; k < found; ++k)
            {
                if (ws.inverseAlphas[k] >= -1e-9 && ws.inverseBetas[k] >= -1e-9 && ws.inverseAlphas[k] + ws.inverseBetas[k] <= 1 + 1e-9)
                {
                    inside = k;
                }
            }
            tOut = double.MinValue;
            hOut = 0;
            if (inside >= 0)
            {
                tIn = 0;
                hIn = ws.inverseHeights[inside];
                if (crossings == 0)
                {
                    return false;
                }
                for (int k = 0; k < crossings; ++k)
                {
                    if (ws.distances[k] > tOut)
                    {
                        tOut = ws.distances[k];
                        hOut = ws.heights[k];
                    }
                }
            }
            else
            {
                if (crossings < 2)
                {
                    return false;
                }
                tIn = double.MaxValue;
                hIn = 0;
                for (int k = 0; k < crossings; ++k)
                {
                    if (ws.distances[k] < tIn)
                    {
                        tIn = ws.distances[k];
                        hIn = ws.heights[k];
                    }
                    if (ws.distances[k] > tOut)
                    {
                        tOut = ws.distances[k];
                        hOut = ws.heights[k];
                    }
                }
            }

            // The ray in the prism as rational functions of the height, with a positive denominator
            prism.RayCoefficients(o, w, ws.ray);
            double hMiddle = 0.5 * (hIn + hOut);
            double[] d = ws.ray.denominator;
            double dMiddle = ShellRay.Polynomial(d, hMiddle);
            double coefficientScale = Math.Abs(d[0]) + Math.Abs(d[1]) + Math.Abs(d[2]);
            if (Math.Abs(dMiddle) <= 1e-14 * coefficientScale)
            {
                // The ray is parallel to the triangles of the prism: its height does not change, and the parameterization degenerates
                return false;
            }
            if (dMiddle < 0)
            {
                for (int c = 0; c < 3; ++c)
                {
                    ws.ray.denominator[c] = -ws.ray.denominator[c];
                    ws.ray.alpha[c] = -ws.ray.alpha[c];
                    ws.ray.beta[c] = -ws.ray.beta[c];
                }
            }

            // The ray in grid coordinates of the displacement map (Ogaki, Eq. 8, with the uv of the base vertices)
            ShellHeights field = mesh.field;
            double tileU = mesh.tileU[triIndex], tileV = mesh.tileV[triIndex];
            int cornerBase = 6 * triIndex;
            for (int c = 0; c < 3; ++c)
            {
                double a = ws.ray.alpha[c], b = ws.ray.beta[c], dd = ws.ray.denominator[c];
                ws.gx[c] = (dd - a - b) * mesh.gridCorners[cornerBase] + a * mesh.gridCorners[cornerBase + 2] + b * mesh.gridCorners[cornerBase + 4];
                ws.gy[c] = (dd - a - b) * mesh.gridCorners[cornerBase + 1] + a * mesh.gridCorners[cornerBase + 3] + b * mesh.gridCorners[cornerBase + 5];
            }

            // Front to back traversal of the quadtree
            double lowest = Math.Min(hIn, hOut), highest = Math.Max(hIn, hOut);
            bool ascending = hOut >= hIn;
            double tBest = tMax;
            int top = field.levels - 1;
            int sp = 0;
            if (!NodeInterval(ws, top, 0, 0, lowest, highest, out double rootLow, out double rootHigh))
            {
                return false;
            }
            ws.stackLevel[sp] = top;
            ws.stackI[sp] = 0;
            ws.stackJ[sp] = 0;
            ws.stackT[sp] = tIn;
            ws.stackLow[sp] = rootLow;
            ws.stackHigh[sp] = rootHigh;
            ++sp;
            bool hit = false;
            while (sp > 0)
            {
                --sp;
                int level = ws.stackLevel[sp], i = ws.stackI[sp], j = ws.stackJ[sp];
                double low = ws.stackLow[sp], high = ws.stackHigh[sp];
                if (ws.stackT[sp] >= tBest)
                {
                    continue;
                }
                if (level == 0 || field.IsFlat(level, i, j))
                {
                    if (IntersectCell(ws, o, w, i << level, j << level, 1 << level, low, high, tileU, tileV, ref tBest, ref best))
                    {
                        hit = true;
                        if (anyHit)
                        {
                            return true;
                        }
                    }
                    continue;
                }
                int children = 0;
                for (int dy = 0; dy < 2; ++dy)
                {
                    for (int dx = 0; dx < 2; ++dx)
                    {
                        int ci = 2 * i + dx, cj = 2 * j + dy;
                        if (ci >= field.LevelWidth(level - 1) || cj >= field.LevelHeight(level - 1))
                        {
                            continue;
                        }
                        if (!NodeInterval(ws, level - 1, ci, cj, low, high, out double nodeLow, out double nodeHigh))
                        {
                            continue;
                        }
                        double enter = ascending ? nodeLow : nodeHigh;
                        double a = ShellRay.Polynomial(ws.ray.alpha, enter), b = ShellRay.Polynomial(ws.ray.beta, enter);
                        double dEnter = ShellRay.Polynomial(ws.ray.denominator, enter);
                        double tEnter = SdfVector.Dot(w, prism.Point(a / dEnter, b / dEnter, enter) - o);
                        int slot = children++;
                        ws.childI[slot] = ci;
                        ws.childJ[slot] = cj;
                        ws.childT[slot] = tEnter;
                        ws.childLow[slot] = nodeLow;
                        ws.childHigh[slot] = nodeHigh;
                    }
                }
                // Push the farthest first so that the nearest is popped first
                for (int pass = 0; pass < children; ++pass)
                {
                    int farthest = 0;
                    for (int k = 1; k < children; ++k)
                    {
                        if (ws.childT[k] > ws.childT[farthest])
                        {
                            farthest = k;
                        }
                    }
                    ws.stackLevel[sp] = level - 1;
                    ws.stackI[sp] = ws.childI[farthest];
                    ws.stackJ[sp] = ws.childJ[farthest];
                    ws.stackT[sp] = ws.childT[farthest];
                    ws.stackLow[sp] = ws.childLow[farthest];
                    ws.stackHigh[sp] = ws.childHigh[farthest];
                    ++sp;
                    ws.childT[farthest] = double.MinValue;
                }
            }
            return hit;
        }

        private void SetPrism(ShellPrism prism)
        {
            prism.Set(mesh.triangleBase[3 * triIndex], mesh.triangleBase[3 * triIndex + 1], mesh.triangleBase[3 * triIndex + 2],
                      mesh.triangleOffset[3 * triIndex], mesh.triangleOffset[3 * triIndex + 1], mesh.triangleOffset[3 * triIndex + 2]);
        }

        // The surface interaction at a hit (see Ogaki, Sec. 4.3, and Hoetzlein, Eq. 10)
        private SurfaceInteraction InteractionFromHit(Ray ray, ShellHit hit)
        {
            ShellWorkspace ws = Workspace();
            int v0 = mesh.indices[3 * triIndex], v1 = mesh.indices[3 * triIndex + 1], v2 = mesh.indices[3 * triIndex + 2];
            ShellPrism prism = ws.prism;
            SetPrism(prism);
            ShellHeights field = mesh.field;
            double alpha = hit.alpha, beta = hit.beta, h = hit.h;
            SdfVector p = prism.Point(alpha, beta, h);

            // The plane of the micro triangle in grid coordinates, as a plane of (alpha, beta, h): the height H of the map is
            // heightBase + heightScale h
            MicroTriangle(hit.cellX, hit.cellY, hit.cellSize, hit.triangle, out double ax, out double ay, out double ah, out double bx,
                          out double by, out double bh, out double cx, out double cy, out double ch);
            double ux = bx - ax, uy = by - ay, uh = bh - ah, vx = cx - ax, vy = cy - ay, vh = ch - ah;
            double nx = uy * vh - uh * vy, ny = uh * vx - ux * vh, nh = (ux * vy - uy * vx) * mesh.heightScale[triIndex];
            Point2f t0 = mesh.uv[v0], t1 = mesh.uv[v1], t2 = mesh.uv[v2];
            double g1x = (double)(t1.x - t0.x) * field.cellsX, g1y = (double)(t1.y - t0.y) * field.cellsY;
            double g2x = (double)(t2.x - t0.x) * field.cellsX, g2y = (double)(t2.y - t0.y) * field.cellsY;
            double nAlpha = nx * g1x + ny * g1y, nBeta = nx * g2x + ny * g2y;

            // The Jacobian of the map from canonical space to the world is [E0 E1 Ns]; normals transform by its inverse transpose
            prism.Edges(h, out SdfVector e0, out SdfVector e1);
            SdfVector ns = prism.Offset(alpha, beta);
            SdfVector cofactor0 = SdfVector.Cross(e1, ns), cofactor1 = SdfVector.Cross(ns, e0), cofactor2 = SdfVector.Cross(e0, e1);
            SdfVector displacedNormal = cofactor0 * nAlpha + cofactor1 * nBeta + cofactor2 * nh;
            if (SdfVector.Dot(displacedNormal, ns) < 0)
            {
                displacedNormal = -displacedNormal;
            }
            displacedNormal = displacedNormal.Normalized();

            // The tangents of the displaced surface along alpha and beta, and along u and v
            SdfVector dXdAlpha = e0 - ns * (nAlpha / nh), dXdBeta = e1 - ns * (nBeta / nh);
            double a = t1.x - t0.x, b = t2.x - t0.x, c = t1.y - t0.y, dv = t2.y - t0.y;
            double determinant = a * dv - b * c;
            SdfVector dpdu, dpdv;
            double uvScale = Math.Abs(a) + Math.Abs(b) + Math.Abs(c) + Math.Abs(dv);
            if (Math.Abs(determinant) > 1e-12 * uvScale * uvScale)
            {
                dpdu = dXdAlpha * (dv / determinant) - dXdBeta * (c / determinant);
                dpdv = dXdBeta * (a / determinant) - dXdAlpha * (b / determinant);
            }
            else
            {
                Vector3f nf = new Vector3f((Float)displacedNormal.x, (Float)displacedNormal.y, (Float)displacedNormal.z);
                Vector3f s, t;
                pbrt.CoordinateSystem(nf, &s, &t);
                dpdu = new SdfVector(s.x, s.y, s.z);
                dpdv = new SdfVector(t.x, t.y, t.z);
            }
            Point2f uv = new Point2f((Float)(t0.x + a * alpha + b * beta), (Float)(t0.y + c * alpha + dv * beta));

            // The shading normal: the displaced normal with the flat normal of the base triangle replaced by the interpolated vertex normal
            SdfVector shadingNormal = displacedNormal;
            if (mesh.smoothNormals)
            {
                SdfVector interpolated = (mesh.shadingN[v0] * (1 - alpha - beta) + mesh.shadingN[v1] * alpha + mesh.shadingN[v2] * beta).Normalized();
                SdfVector flat = prism.TriangleNormal(h).Normalized();
                if (SdfVector.Dot(flat, ns) < 0)
                {
                    flat = -flat;
                }
                SdfVector corrected = displacedNormal - flat + interpolated;
                if (corrected.LengthSquared() > 1e-12)
                {
                    shadingNormal = corrected.Normalized();
                }
            }

            bool flip = mesh.reverseOrientation;
            Vector3f pError = pbrt.gamma(8) * pbrt.Abs(new Vector3f((Float)p.x, (Float)p.y, (Float)p.z));
            SurfaceInteraction si = new SurfaceInteraction(
                new Point3fi(new Point3f((Float)p.x, (Float)p.y, (Float)p.z), pError), uv, -ray.d, ToVector(dpdu), ToVector(dpdv),
                new Normal3f(0, 0, 0), new Normal3f(0, 0, 0), ray.time, false);
            SdfVector geometric = flip ? -displacedNormal : displacedNormal;
            SdfVector shading = flip ? -shadingNormal : shadingNormal;
            si.n = new Normal3f((Float)geometric.x, (Float)geometric.y, (Float)geometric.z);
            si.SetShadingGeometry(new Normal3f((Float)shading.x, (Float)shading.y, (Float)shading.z), ToVector(dpdu), ToVector(dpdv),
                                  new Normal3f(0, 0, 0), new Normal3f(0, 0, 0), true);
            return si;
        }

        private static Vector3f ToVector(SdfVector v)
        {
            return new Vector3f((Float)v.x, (Float)v.y, (Float)v.z);
        }

        // DisplacedTriangle Private Members
        private readonly DisplacedMesh mesh;
        private readonly int triIndex;
    }

}  // namespace pbrt
