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
// Parameters (as for "trianglemesh"; "plymesh" is read with "string filename"):
//   "point3 P", "integer indices", "normal N" (optional; area weighted vertex normals otherwise), "point2 uv" (required),
//   "texture displacement" (required: any float texture, evaluated at the uv of the grid points), "float scale" (1), "float offset" (0),
//   "integer resolution" (the grid is resolution x resolution points, default 1024), "bool smoothnormals" (true: the shading normal keeps
//   the smoothness of the interpolated vertex normals, Hoetzlein 2025, Eq. 10).
// The displacement is along the vertex normals in object space, by scale * texture + offset. The uv of the base triangles should lie
// within one tile of the map (tiles are repeated by the integer part of the uv of the triangle).

using System;
using System.Collections.Generic;

namespace pbrt
{

    // ShellHeightField Definition: the displacement map as normalized heights in [0, 1] on a grid of points, with a pyramid of the
    // minimum and the maximum over the cells (a cell is the square between four neighbouring points)
    public sealed class ShellHeightField
    {
        // ShellHeightField Public Methods
        public ShellHeightField(int width, int height, float[] heights)
        {
            this.width = width;
            this.height = height;
            this.heights = heights;
            cellsX = width - 1;
            cellsY = height - 1;
            int levelCount = 1;
            int sizeX = cellsX, sizeY = cellsY;
            while (sizeX > 1 || sizeY > 1)
            {
                sizeX = (sizeX + 1) / 2;
                sizeY = (sizeY + 1) / 2;
                ++levelCount;
            }
            levels = levelCount;
            levelWidth = new int[levels];
            levelHeight = new int[levels];
            minimum = new float[levels][];
            maximum = new float[levels][];
            levelWidth[0] = cellsX;
            levelHeight[0] = cellsY;
            minimum[0] = new float[cellsX * cellsY];
            maximum[0] = new float[cellsX * cellsY];
            for (int j = 0; j < cellsY; ++j)
            {
                for (int i = 0; i < cellsX; ++i)
                {
                    float h00 = Sample(i, j), h10 = Sample(i + 1, j), h01 = Sample(i, j + 1), h11 = Sample(i + 1, j + 1);
                    minimum[0][j * cellsX + i] = Math.Min(Math.Min(h00, h10), Math.Min(h01, h11));
                    maximum[0][j * cellsX + i] = Math.Max(Math.Max(h00, h10), Math.Max(h01, h11));
                }
            }
            for (int level = 1; level < levels; ++level)
            {
                int w = (levelWidth[level - 1] + 1) / 2, h = (levelHeight[level - 1] + 1) / 2;
                levelWidth[level] = w;
                levelHeight[level] = h;
                minimum[level] = new float[w * h];
                maximum[level] = new float[w * h];
                for (int j = 0; j < h; ++j)
                {
                    for (int i = 0; i < w; ++i)
                    {
                        float low = float.MaxValue, high = float.MinValue;
                        for (int dy = 0; dy < 2; ++dy)
                        {
                            for (int dx = 0; dx < 2; ++dx)
                            {
                                int ci = 2 * i + dx, cj = 2 * j + dy;
                                if (ci < levelWidth[level - 1] && cj < levelHeight[level - 1])
                                {
                                    low = Math.Min(low, minimum[level - 1][cj * levelWidth[level - 1] + ci]);
                                    high = Math.Max(high, maximum[level - 1][cj * levelWidth[level - 1] + ci]);
                                }
                            }
                        }
                        minimum[level][j * w + i] = low;
                        maximum[level][j * w + i] = high;
                    }
                }
            }
        }

        public float Sample(int x, int y)
        {
            return heights[y * width + x];
        }

        public int LevelWidth(int level)
        {
            return levelWidth[level];
        }

        public int LevelHeight(int level)
        {
            return levelHeight[level];
        }

        public void NodeRange(int level, int i, int j, out double low, out double high)
        {
            low = minimum[level][j * levelWidth[level] + i];
            high = maximum[level][j * levelWidth[level] + i];
        }

        // The height of the surface made of the micro triangles, at the grid position (gx, gy)
        public double Value(double gx, double gy)
        {
            gx = Math.Min(Math.Max(gx, 0), cellsX);
            gy = Math.Min(Math.Max(gy, 0), cellsY);
            int i = Math.Min((int)Math.Floor(gx), cellsX - 1), j = Math.Min((int)Math.Floor(gy), cellsY - 1);
            double fx = gx - i, fy = gy - j;
            double h00 = Sample(i, j), h10 = Sample(i + 1, j), h01 = Sample(i, j + 1), h11 = Sample(i + 1, j + 1);
            if (fx >= fy)
            {
                return h00 + fx * (h10 - h00) + fy * (h11 - h10);
            }
            return h00 + fx * (h11 - h01) + fy * (h01 - h00);
        }

        // ShellHeightField Public Members
        public readonly int width, height, cellsX, cellsY, levels;

        // ShellHeightField Private Members
        private readonly float[] heights;
        private readonly int[] levelWidth, levelHeight;
        private readonly float[][] minimum, maximum;
    }

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
        public readonly double[] inverseHeights = new double[3];
        public readonly double[] inverseAlphas = new double[3];
        public readonly double[] inverseBetas = new double[3];
        public readonly int[] stackLevel = new int[256];
        public readonly int[] stackI = new int[256];
        public readonly int[] stackJ = new int[256];
        public readonly double[] stackT = new double[256];
        public readonly double[] stackLow = new double[256];
        public readonly double[] stackHigh = new double[256];
    }

    // ShellHit Definition
    internal struct ShellHit
    {
        public double t, h, alpha, beta;
        public int cellX, cellY, triangle;
        public double tileU, tileV;
    }

    // DisplacedMesh Definition
    public sealed class DisplacedMesh
    {
        // DisplacedMesh Public Methods
        public DisplacedMesh(Transform renderFromObject, bool reverseOrientation, int[] indices, Point3f[] p, Normal3f[] n, Point2f[] uv,
                             ShellHeightField field, double minimumDisplacement, double maximumDisplacement, bool smoothNormals)
        {
            this.indices = indices;
            this.uv = uv;
            this.field = field;
            this.reverseOrientation = reverseOrientation;
            this.smoothNormals = smoothNormals;
            int vertexCount = p.Length;
            baseP = new SdfVector[vertexCount];
            offset = new SdfVector[vertexCount];
            shadingN = new SdfVector[vertexCount];
            for (int i = 0; i < vertexCount; ++i)
            {
                Normal3f unit = pbrt.Normalize(n[i]);
                Vector3f up = new Vector3f(unit.x, unit.y, unit.z);
                Point3f bottom = p[i] + up * (Float)minimumDisplacement;
                Vector3f height = up * (Float)(maximumDisplacement - minimumDisplacement);
                Point3f renderBottom = renderFromObject.Apply(bottom);
                Vector3f renderHeight = renderFromObject.Apply(height);
                baseP[i] = new SdfVector(renderBottom.x, renderBottom.y, renderBottom.z);
                offset[i] = new SdfVector(renderHeight.x, renderHeight.y, renderHeight.z);
                Normal3f renderNormal = pbrt.Normalize(renderFromObject.Apply(unit));
                shadingN[i] = new SdfVector(renderNormal.x, renderNormal.y, renderNormal.z);
            }
        }

        public int TriangleCount()
        {
            return indices.Length / 3;
        }

        // DisplacedMesh Public Members
        public readonly int[] indices;
        public readonly SdfVector[] baseP, offset, shadingN;
        public readonly Point2f[] uv;
        public readonly ShellHeightField field;
        public readonly bool reverseOrientation, smoothNormals;
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
                int v = mesh.indices[3 * triIndex + k];
                for (int level = 0; level < 2; ++level)
                {
                    SdfVector q = mesh.baseP[v] + mesh.offset[v] * level;
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
            return (Float)(0.5 * SdfVector.Cross(mesh.baseP[i1] - mesh.baseP[i0], mesh.baseP[i2] - mesh.baseP[i0]).Length());
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

        // Creates the triangles of the mesh from the parameters
        public static pstd.vector<Shape> Create(Transform renderFromObject, bool reverseOrientation, ParameterDictionary parameters,
                                                IDictionary<string, FloatTexture> floatTextures, FileLoc loc)
        {
            List<int> indexList;
            List<Point3f> pointList;
            List<Normal3f> normalList;
            List<Point2f> uvList;
            string filename = parameters.GetOneString("filename", "");
            if (filename.Length != 0)
            {
                TriQuadMesh ply = TriQuadMesh.ReadPLY(pbrt.ResolveFilename(filename));
                ply.ConvertToOnlyTriangles();
                indexList = new List<int>(ply.triIndices.ToArray());
                pointList = new List<Point3f>(ply.p.ToArray());
                normalList = new List<Normal3f>(ply.n.ToArray());
                uvList = new List<Point2f>(ply.uv.ToArray());
            }
            else
            {
                indexList = parameters.GetIntArray("indices");
                pointList = parameters.GetPoint3fArray("P");
                normalList = parameters.GetNormal3fArray("N");
                uvList = parameters.GetPoint2fArray("uv");
            }
            if (pointList.Count == 0 || indexList.Count == 0 || indexList.Count % 3 != 0)
            {
                pbrt.ErrorExit(loc, "displacedmesh: \"P\" and \"indices\" (a multiple of three) are required.");
            }
            if (uvList.Count != pointList.Count)
            {
                pbrt.ErrorExit(loc, "displacedmesh: \"uv\" is required, one per vertex.");
            }
            Point3f[] p = pointList.ToArray();
            int[] indices = indexList.ToArray();
            Normal3f[] n = normalList.Count == p.Length ? normalList.ToArray() : VertexNormals(p, indices);
            string textureName = parameters.GetTexture("displacement");
            if (textureName.Length == 0 || !floatTextures.TryGetValue(textureName, out FloatTexture texture))
            {
                pbrt.ErrorExit(loc, "displacedmesh: \"texture displacement\" must name a float texture.");
                return null;
            }
            double scale = parameters.GetOneFloat("scale", 1), offsetValue = parameters.GetOneFloat("offset", 0);
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
            double low = double.MaxValue, high = double.MinValue;
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
            ShellHeightField field = new ShellHeightField(resolution, resolution, samples);
            DisplacedMesh mesh = new DisplacedMesh(renderFromObject, reverseOrientation, indices, p, n, uvList.ToArray(), field, low, high,
                                                   parameters.GetOneBool("smoothnormals", true));
            pstd.vector<Shape> shapes = new pstd.vector<Shape>();
            for (int i = 0; i < mesh.TriangleCount(); ++i)
            {
                shapes.push_back(new DisplacedTriangle(mesh, i));
            }
            return shapes;
        }

        // DisplacedTriangle Private Methods

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

        // The interval of heights in which the ray is inside the box of a node of the quadtree (conservatively: the hull of the
        // heights inside), clipped to the range [lowest, highest] of the heights of the ray in the prism
        private bool NodeInterval(ShellWorkspace ws, int level, int i, int j, double lowest, double highest, out double low, out double high)
        {
            low = lowest;
            high = highest;
            mesh.field.NodeRange(level, i, j, out double nodeLow, out double nodeHigh);
            const double slack = 1e-6;
            low = Math.Max(low, nodeLow - slack);
            high = Math.Min(high, nodeHigh + slack);
            if (low > high)
            {
                return false;
            }
            double x0 = (double)i * (1 << level), x1 = (double)(i + 1) * (1 << level);
            double y0 = (double)j * (1 << level), y1 = (double)(j + 1) * (1 << level);
            double[] d = ws.ray.denominator;
            return Hull(ws.gx[0] - x0 * d[0], ws.gx[1] - x0 * d[1], ws.gx[2] - x0 * d[2], ref low, ref high, ws.quadraticRoots) &&
                   Hull(x1 * d[0] - ws.gx[0], x1 * d[1] - ws.gx[1], x1 * d[2] - ws.gx[2], ref low, ref high, ws.quadraticRoots) &&
                   Hull(ws.gy[0] - y0 * d[0], ws.gy[1] - y0 * d[1], ws.gy[2] - y0 * d[2], ref low, ref high, ws.quadraticRoots) &&
                   Hull(y1 * d[0] - ws.gy[0], y1 * d[1] - ws.gy[1], y1 * d[2] - ws.gy[2], ref low, ref high, ws.quadraticRoots);
        }

        // The vertices of a micro triangle of the cell (i, j) in (grid x, grid y, height)
        private void MicroTriangle(int i, int j, int triangle, out double ax, out double ay, out double ah, out double bx, out double by,
                                   out double bh, out double cx, out double cy, out double ch)
        {
            ShellHeightField field = mesh.field;
            double h00 = field.Sample(i, j), h10 = field.Sample(i + 1, j), h01 = field.Sample(i, j + 1), h11 = field.Sample(i + 1, j + 1);
            ax = i;
            ay = j;
            ah = h00;
            if (triangle == 0)
            {
                bx = i + 1;
                by = j;
                bh = h10;
                cx = i + 1;
                cy = j + 1;
                ch = h11;
            }
            else
            {
                bx = i + 1;
                by = j + 1;
                bh = h11;
                cx = i;
                cy = j + 1;
                ch = h01;
            }
        }

        // Intersects the two micro triangles of a leaf with the ray; updates the hit when one is closer than tBest
        private bool IntersectCell(ShellWorkspace ws, SdfVector o, SdfVector w, int i, int j, double low, double high, double tileU,
                                   double tileV, ref double tBest, ref ShellHit best)
        {
            bool found = false;
            double[] d = ws.ray.denominator;
            for (int triangle = 0; triangle < 2; ++triangle)
            {
                MicroTriangle(i, j, triangle, out double ax, out double ay, out double ah, out double bx, out double by, out double bh,
                              out double cx, out double cy, out double ch);
                // The plane of the micro triangle: nx x + ny y + nh h + k = 0
                double ux = bx - ax, uy = by - ay, uh = bh - ah, vx = cx - ax, vy = cy - ay, vh = ch - ah;
                double nx = uy * vh - uh * vy, ny = uh * vx - ux * vh, nh = ux * vy - uy * vx;
                double k = -(nx * ax + ny * ay + nh * ah);
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
                    double fx = gx - i, fy = gy - j;
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
            if (dirLength == 0)
            {
                return false;
            }
            SdfVector w = direction / dirLength;
            double tMax = double.IsInfinity(tMaxRay) ? double.MaxValue : tMaxRay * dirLength;
            ShellWorkspace ws = Workspace();
            int v0 = mesh.indices[3 * triIndex], v1 = mesh.indices[3 * triIndex + 1], v2 = mesh.indices[3 * triIndex + 2];
            ShellPrism prism = ws.prism;
            prism.Set(mesh.baseP[v0], mesh.baseP[v1], mesh.baseP[v2], mesh.offset[v0], mesh.offset[v1], mesh.offset[v2]);

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
            ShellHeightField field = mesh.field;
            Point2f t0 = mesh.uv[v0], t1 = mesh.uv[v1], t2 = mesh.uv[v2];
            double tileU = Math.Floor(((double)t0.x + t1.x + t2.x) / 3), tileV = Math.Floor(((double)t0.y + t1.y + t2.y) / 3);
            double g0x = (t0.x - tileU) * field.cellsX, g0y = (t0.y - tileV) * field.cellsY;
            double g1x = (t1.x - tileU) * field.cellsX, g1y = (t1.y - tileV) * field.cellsY;
            double g2x = (t2.x - tileU) * field.cellsX, g2y = (t2.y - tileV) * field.cellsY;
            for (int c = 0; c < 3; ++c)
            {
                double a = ws.ray.alpha[c], b = ws.ray.beta[c], dd = ws.ray.denominator[c];
                ws.gx[c] = (dd - a - b) * g0x + a * g1x + b * g2x;
                ws.gy[c] = (dd - a - b) * g0y + a * g1y + b * g2y;
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
            int[] childI = new int[4], childJ = new int[4];
            double[] childT = new double[4], childLow = new double[4], childHigh = new double[4];
            while (sp > 0)
            {
                --sp;
                int level = ws.stackLevel[sp], i = ws.stackI[sp], j = ws.stackJ[sp];
                double low = ws.stackLow[sp], high = ws.stackHigh[sp];
                if (ws.stackT[sp] >= tBest)
                {
                    continue;
                }
                if (level == 0)
                {
                    if (IntersectCell(ws, o, w, i, j, low, high, tileU, tileV, ref tBest, ref best))
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
                        childI[slot] = ci;
                        childJ[slot] = cj;
                        childT[slot] = tEnter;
                        childLow[slot] = nodeLow;
                        childHigh[slot] = nodeHigh;
                    }
                }
                // Push the farthest first so that the nearest is popped first
                for (int pass = 0; pass < children; ++pass)
                {
                    int farthest = 0;
                    for (int k = 1; k < children; ++k)
                    {
                        if (childT[k] > childT[farthest])
                        {
                            farthest = k;
                        }
                    }
                    ws.stackLevel[sp] = level - 1;
                    ws.stackI[sp] = childI[farthest];
                    ws.stackJ[sp] = childJ[farthest];
                    ws.stackT[sp] = childT[farthest];
                    ws.stackLow[sp] = childLow[farthest];
                    ws.stackHigh[sp] = childHigh[farthest];
                    ++sp;
                    childT[farthest] = double.MinValue;
                }
            }
            return hit;
        }

        // The surface interaction at a hit (see Ogaki, Sec. 4.3, and Hoetzlein, Eq. 10)
        private SurfaceInteraction InteractionFromHit(Ray ray, ShellHit hit)
        {
            ShellWorkspace ws = Workspace();
            int v0 = mesh.indices[3 * triIndex], v1 = mesh.indices[3 * triIndex + 1], v2 = mesh.indices[3 * triIndex + 2];
            ShellPrism prism = ws.prism;
            prism.Set(mesh.baseP[v0], mesh.baseP[v1], mesh.baseP[v2], mesh.offset[v0], mesh.offset[v1], mesh.offset[v2]);
            ShellHeightField field = mesh.field;
            double alpha = hit.alpha, beta = hit.beta, h = hit.h;
            SdfVector p = prism.Point(alpha, beta, h);

            // The plane of the micro triangle in grid coordinates, as a plane of (alpha, beta, h)
            MicroTriangle(hit.cellX, hit.cellY, hit.triangle, out double ax, out double ay, out double ah, out double bx, out double by,
                          out double bh, out double cx, out double cy, out double ch);
            double ux = bx - ax, uy = by - ay, uh = bh - ah, vx = cx - ax, vy = cy - ay, vh = ch - ah;
            double nx = uy * vh - uh * vy, ny = uh * vx - ux * vh, nh = ux * vy - uy * vx;
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
