// (not in pbrt-v4) sdfshape.cs
//
// Shape "sdf": a surface defined by a signed distance field (see sdf.cs), the counterpart of OctaneRender's Vectron. Rays are
// intersected by sphere tracing inside a user supplied box, in double precision and in object space, with the over-relaxation of
// Keinert et al. (Enhanced Sphere Tracing, 2014) and a Newton polish of the hit. The surface is the zero set; the field is
// negative inside, so the shape is a closed volume boundary for the boundary stack (mediumpriority, material volumes) and an
// area light shape (uniform sampling by area, see sdfsampling.cs).
//
//   Shape "sdf" "string sdf" "smoothunion(sphere(1), translate(box(.5,.5,.5), 1,0,0), .3)"
//         "point3 pmin" [-2 -2 -2] "point3 pmax" [3 2 2]
//
// The box may be left out: the field then gives its own (SdfNode.Bounds), which every primitive with an end and the operators over
// them know. Any other field with an end is carved for its box from the sphere of "float carveradius" (10) about the origin
// (Sphere Carving, sdfcarving.cs). A field with a part without end (a plane, an unlimited repeat), a fractal or a mesh needs the
// box. "bool carve" (false) carves a convex bound of "integer carveplanes" (20) planes around the object inside the box and
// replaces the field by the distance to it outside of it, which is cheaper for a costly field ("float carvethreshold", 0.05 of the
// size of the box, is the distance from the surface at which the carving stops; "integer carvespheres", 4000, its budget).
// "string carveuse" chooses what the bound is used for: "clip" (the default: rays are cut to the bound like to the box, with no
// cost for an evaluation) or "proxy" (the paper: the field becomes the distance to the bound outside of it, at the cost of the
// planes for every evaluation; slower than "clip" in the renders that were timed, and slower than no carving for a cheap field).
// "integer carvehulls" (1) cuts the bound into up to that many convex pieces (the approximate convex decomposition of the paper),
// which fits an object with holes or with separate parts more closely.
//
// Optional: "float epsilon" (the hit tolerance relative to the box, 1e-8), "integer maxsteps" (32768), "float relaxation" (1.2, 1
// is plain sphere tracing), "float lipschitz" (a factor on the Lipschitz bound that shortens the steps, 1), "string uvmapping"
// ("spherical", "cylindrical", "planar", "box"), "integer tessellation" (the cells along the longest side of the box used to find
// the surface for light sampling, 64), "integer areasubdivisions" (3) and "bool analyticgradient" (false: normals from the
// closed-form gradients of the nodes instead of a central difference) and "float normalfootprint" (0: the angle of a pixel in
// radians, which makes the offset of a normal's central difference grow with the distance of the hit along its ray).
// "float hitfootprint" (0) is the same angle for the tolerance of a hit: a ray stops once the surface is nearer than that times the
// distance along the ray. "integer dda" (0) makes a grid of that many cells along the longest side of the box, marks the cells
// that the surface may cross (by the Lipschitz bound of the field), and walks rays through it cell by cell so that the field is
// only asked in the marked ones (it needs a field whose bound is proven). "bool cull" (false) compiles a chain of plain unions
// with eight or more operands as a bounding volume hierarchy, so that the operands that are too far to matter are not evaluated.
// "bool sloperelaxation" (false) chooses the relaxation factor of each step from how the distance changed over the last one, up to
// "relaxation" (try 2), instead of using "relaxation" for every step. "bool warpbounds" (false) gives twists and bends a Lipschitz
// bound that depends on the distance from their axis, for longer steps near it.
// "integer proxies" (0) puts a proxy around every part of at least that many nodes that has a box and sits below nothing but
// unions, intersections, subtractions and transforms: away from its box such a part answers with the distance to the box.
// Below a smooth union a proxy keeps the field of its part out to the widths of the blends above it, which leaves the solid as
// it is.
// "bool legacyphases" (false) makes the multiphase functions build the nodes of sdfphases.cs instead of the bundles of ordinary
// fields of sdfphasetree.cs (the textures "sdftouch" and "sdflabel" take it too).
// "bool raybounds" (false) lets a ray step by the bound of the field along its own direction, found once for each stretch of
// the ray: for a twist or a bend that is the plain bound of the child for rays across the axis.
// "bool prunehierarchy" (true, with "prune") cuts the fields of the cells down level by level, from blocks of cells to the cells,
// instead of every cell from the whole field.
// "bool prune" (false, needs "dda") gives every marked cell of the grid a field of its own with only the operands of the unions,
// intersections and blends that can matter inside the cell, which is what rays and normals then ask there.

using System;
using System.Collections.Generic;

namespace pbrt
{

    // SdfUvMapping Definition
    public enum SdfUvMapping
    {
        Spherical,
        Cylindrical,
        Planar,
        Box,
        PlanarYZ,
        PlanarXZ
    }

    // SdfShapeOptions Definition
    public sealed class SdfShapeOptions
    {
        public double epsilon = 1e-8;
        public int maxSteps = 32768;
        public double relaxation = 1.2;
        public double validation = 2;
        public double lipschitzFactor = 1;
        public SdfUvMapping uvMapping = SdfUvMapping.Spherical;
        public int tessellation = 64;
        public int areaSubdivisions = 3;
        // Whether an area light picks its sample points in proportion to the importance of the triangles for the receiving point
        // (otherwise uniformly by area)
        public bool importanceSampling = true;
        // Whether normals and the Newton polish use the closed-form gradients of the nodes (SdfNode.AnalyticGradient) instead of a
        // central difference of the whole field
        public bool analyticGradient = false;
        // The angle of a pixel in radians: the offset of the central difference of a normal is this times the distance of the hit
        // along its ray (0 keeps the fixed small offset)
        public double normalFootprint = 0;
        // Convex volumes whose union holds the surface, each given by planes of four numbers (n . p <= offset inside): rays are
        // cut to them like to the box (null for none)
        public double[][] clipHulls = null;
        // The angle of a pixel in radians: a ray stops once the surface is nearer than this times the distance along the ray (0
        // keeps the fixed tolerance)
        public double hitFootprint = 0;
        // The number of cells along the longest side of the box of a grid of the cells that the surface may cross, which rays
        // walk to skip the others (0 for no grid)
        public int ddaResolution = 0;
        // With the grid: a cut-down field for each of its cells, with only the operands that matter there (and whether these are
        // compiled)
        public bool prune = false;
        // Whether the relaxation factor of a step comes from how the distance changed over the last step, up to "relaxation",
        // instead of being "relaxation" for every step
        public bool slopeRelaxation = false;
        // Whether a ray steps by the bound of the field along its own direction (SdfNode.RayLipschitz) where that is smaller
        public bool rayBounds = false;
        // Whether the shape counts the values that marching asks for (SdfShape.marchEvaluations): for tests, since the count is
        // one place in memory that every thread would write to
        public bool countEvaluations = false;
        public bool pruneCompile = true;
        // The number of hits in a cell after which the field of the cell is compiled
        public int pruneCompileHits = 16;
        // Whether the fields of the cells are cut down level by level: a block of cells first, then each half of it along every
        // axis from the field of the block, down to the cells
        public bool pruneHierarchy = true;
    }

    // SdfShape Definition
    public sealed unsafe class SdfShape : Shape
    {
        // SdfShape Public Methods
        public SdfShape(Transform renderFromObject, Transform objectFromRender, bool reverseOrientation, SdfNode field,
                        Point3f lower, Point3f upper, SdfShapeOptions options)
        {
            this.renderFromObject = renderFromObject;
            this.objectFromRender = objectFromRender;
            this.reverseOrientation = reverseOrientation;
            transformSwapsHandedness = renderFromObject.SwapsHandedness();
            this.field = field;
            this.options = options;
            this.lower = new SdfVector(lower.x, lower.y, lower.z);
            this.upper = new SdfVector(upper.x, upper.y, upper.z);
            lipschitz = field.Lipschitz() * options.lipschitzFactor;
            localBounds = field.HasLocalBounds();
            rigorousField = field.IsRigorous();
            SdfVector extent = this.upper - this.lower;
            scale = extent.Length();
            epsilon = options.epsilon * scale;
            gradientStep = 1e-6 * scale;
            objectFromRenderMatrix = Matrix3x4(objectFromRender);
            renderFromObjectMatrix = Matrix3x4(renderFromObject);
            if (options.ddaResolution > 0 && rigorousField)
            {
                if (options.prune && options.pruneHierarchy && options.lipschitzFactor == 1)
                {
                    PruneBlocks(options.ddaResolution, options.pruneCompile);
                }
                else
                {
                    BuildCells(options.ddaResolution);
                    if (options.prune)
                    {
                        PruneCells(options.pruneCompile);
                    }
                }
            }
        }

        private static double[] Matrix3x4(Transform t)
        {
            SquareMatrix4 m = t.GetMatrix();
            double[] r = new double[12];
            for (int i = 0; i < 3; ++i)
            {
                for (int j = 0; j < 4; ++j)
                {
                    r[4 * i + j] = m[i][j];
                }
            }
            return r;
        }

        public SdfNode Field()
        {
            return field;
        }

        public double LipschitzBound()
        {
            return lipschitz;
        }

        public override Bounds3f Bounds()
        {
            return renderFromObject.Apply(new Bounds3f(new Point3f((Float)lower.x, (Float)lower.y, (Float)lower.z),
                                                       new Point3f((Float)upper.x, (Float)upper.y, (Float)upper.z)));
        }

        public override DirectionCone NormalBounds()
        {
            return DirectionCone.EntireSphere();
        }

        // Sphere tracing

        // Traces a ray between the distances u0 and u1. With the convex volumes of a carved bound (options.clipHulls) only the
        // parts of the ray inside one of them are traced, from the nearest on: the surface is inside their union.
        private bool March(SdfVector o, SdfVector w, double u0, double u1, bool polish, out double uHit)
        {
            double[][] hulls = options.clipHulls;
            if (hulls == null)
            {
                return MarchCells(o, w, u0, u1, polish, out uHit);
            }
            int count = 0;
            Span<double> from = stackalloc double[hulls.Length];
            Span<double> to = stackalloc double[hulls.Length];
            for (int h = 0; h < hulls.Length; ++h)
            {
                double[] planes = hulls[h];
                double near = u0, far = u1;
                for (int i = 0; i < planes.Length && near <= far; i += 4)
                {
                    double along = planes[i] * w.x + planes[i + 1] * w.y + planes[i + 2] * w.z;
                    double room = planes[i + 3] - (planes[i] * o.x + planes[i + 1] * o.y + planes[i + 2] * o.z);
                    if (along > 0)
                    {
                        far = Math.Min(far, room / along);
                    }
                    else if (along < 0)
                    {
                        near = Math.Max(near, room / along);
                    }
                    else if (room < 0)
                    {
                        far = near - 1;
                    }
                }
                if (near <= far)
                {
                    // Kept in the order of their start
                    int at = count++;
                    while (at > 0 && from[at - 1] > near)
                    {
                        from[at] = from[at - 1];
                        to[at] = to[at - 1];
                        --at;
                    }
                    from[at] = near;
                    to[at] = far;
                }
            }
            for (int i = 0; i < count; )
            {
                // Spans that overlap are traced as one
                double near = from[i], far = to[i];
                for (++i; i < count && from[i] <= far; ++i)
                {
                    far = Math.Max(far, to[i]);
                }
                if (MarchCells(o, w, near, far, polish, out uHit))
                {
                    return true;
                }
            }
            uHit = 0;
            return false;
        }

        // Traces a ray given in object space with a unit direction between the distances u0 and u1; returns the distance of the hit.
        // Over-relaxed sphere tracing (Keinert et al. 2014): a step is omega times the radius of the safe sphere, and when the new
        // sphere does not overlap the previous one the step is taken back to the unrelaxed one. The step is signed, so a step that
        // overshoots the surface is followed by one that comes back, from either side of the surface.
        // Traces a ray between u0 and u1 through the grid of cells that the surface may cross (options.ddaResolution): the cells
        // along the ray are walked one by one, as in the traversal of a voxel grid by Amanatides and Woo, and only the runs of
        // cells that may hold the surface are traced, from the nearest on. The others cost no value of the field.
        private bool MarchCells(SdfVector o, SdfVector w, double u0, double u1, bool polish, out double uHit)
        {
            if (occupied == null)
            {
                return MarchSpan(o, w, u0, u1, polish, out uHit);
            }
            uHit = 0;
            // The cell of the point a little way into the span, and for each axis the distance at which the ray leaves it
            SdfVector start = o + w * (u0 + 1e-9 * (u1 - u0));
            int ix = Math.Min(Math.Max((int)Math.Floor((start.x - lower.x) / cellSize.x), 0), cellsX - 1);
            int iy = Math.Min(Math.Max((int)Math.Floor((start.y - lower.y) / cellSize.y), 0), cellsY - 1);
            int iz = Math.Min(Math.Max((int)Math.Floor((start.z - lower.z) / cellSize.z), 0), cellsZ - 1);
            int stepX = w.x > 0 ? 1 : -1, stepY = w.y > 0 ? 1 : -1, stepZ = w.z > 0 ? 1 : -1;
            double deltaX = w.x != 0 ? cellSize.x / Math.Abs(w.x) : double.PositiveInfinity;
            double deltaY = w.y != 0 ? cellSize.y / Math.Abs(w.y) : double.PositiveInfinity;
            double deltaZ = w.z != 0 ? cellSize.z / Math.Abs(w.z) : double.PositiveInfinity;
            double nextX = w.x != 0 ? (lower.x + (ix + (w.x > 0 ? 1 : 0)) * cellSize.x - o.x) / w.x : double.PositiveInfinity;
            double nextY = w.y != 0 ? (lower.y + (iy + (w.y > 0 ? 1 : 0)) * cellSize.y - o.y) / w.y : double.PositiveInfinity;
            double nextZ = w.z != 0 ? (lower.z + (iz + (w.z > 0 ? 1 : 0)) * cellSize.z - o.z) / w.z : double.PositiveInfinity;
            double from = u0, runStart = -1;
            while (true)
            {
                double to = Math.Min(Math.Min(nextX, nextY), Math.Min(nextZ, u1));
                if (occupied[(iz * cellsY + iy) * cellsX + ix])
                {
                    if (runStart < 0)
                    {
                        runStart = from;
                    }
                }
                else if (runStart >= 0)
                {
                    if (MarchSpan(o, w, runStart, from, polish, out uHit))
                    {
                        return true;
                    }
                    runStart = -1;
                }
                if (to >= u1)
                {
                    break;
                }
                // Into the next cell across the nearest of the three walls
                if (nextX <= nextY && nextX <= nextZ)
                {
                    ix += stepX;
                    nextX += deltaX;
                    if (ix < 0 || ix >= cellsX)
                    {
                        break;
                    }
                }
                else if (nextY <= nextZ)
                {
                    iy += stepY;
                    nextY += deltaY;
                    if (iy < 0 || iy >= cellsY)
                    {
                        break;
                    }
                }
                else
                {
                    iz += stepZ;
                    nextZ += deltaZ;
                    if (iz < 0 || iz >= cellsZ)
                    {
                        break;
                    }
                }
                from = to;
            }
            if (runStart >= 0)
            {
                return MarchSpan(o, w, runStart, u1, polish, out uHit);
            }
            uHit = 0;
            return false;
        }

        // The field to ask at p: that of the cell of p where the cells have their own cut-down fields, the whole field elsewhere
        private SdfNode FieldAt(SdfVector p)
        {
            if (cellFields == null)
            {
                return field;
            }
            int ix = (int)Math.Floor((p.x - lower.x) / cellSize.x), iy = (int)Math.Floor((p.y - lower.y) / cellSize.y);
            int iz = (int)Math.Floor((p.z - lower.z) / cellSize.z);
            if (ix < 0 || iy < 0 || iz < 0 || ix >= cellsX || iy >= cellsY || iz >= cellsZ)
            {
                return field;
            }
            int index = (iz * cellsY + iy) * cellsX + ix;
            SdfNode cell = cellFields[index];
            if (cell == null)
            {
                return field;
            }
            return cell;
        }

        // Counts a hit in the cell of p and, once the cell has had enough of them, gives it the compiled form of its field.
        // Compiling a field takes about as long as 300000 values of it save, and a field serves several cells: a cell that
        // few rays hit keeps the field that is walked, and a short render does not pay for functions it hardly calls. Every
        // 64 hits a cell also takes the compiled form if another cell with the same field has had it made.
        private void CountHit(SdfVector p)
        {
            int ix = (int)Math.Floor((p.x - lower.x) / cellSize.x), iy = (int)Math.Floor((p.y - lower.y) / cellSize.y);
            int iz = (int)Math.Floor((p.z - lower.z) / cellSize.z);
            if (ix < 0 || iy < 0 || iz < 0 || ix >= cellsX || iy >= cellsY || iz >= cellsZ)
            {
                return;
            }
            int index = (iz * cellsY + iy) * cellsX + ix;
            SdfNode cell = cellFields[index];
            if (cell == null || cell is SdfCompiledNode)
            {
                return;
            }
            int hits = ++cellHits[index];
            if (hits >= options.pruneCompileHits)
            {
                cellFields[index] = compiledCells.GetOrAdd(cell, (SdfNode source) =>
                {
                    return SdfCompiledNode.Compile(source);
                });
            }
            else if ((hits & 63) == 0 && compiledCells.TryGetValue(cell, out SdfNode made))
            {
                cellFields[index] = made;
            }
        }

        // A cut-down field for every cell that the surface may cross (SdfNode.Prune over the ball about the cell), compiled once
        // for each different one. Rays, their polish and the normals then ask the field of the cell that the point is in, which
        // has the values of the whole field there.
        private void PruneCells(bool compile)
        {
            double reach = 0.5 * cellSize.Length() * 1.001 + 4 * gradientStep;
            pruneReach = reach;
            cellFields = new SdfNode[occupied.Length];
            Dictionary<string, SdfNode> made = new Dictionary<string, SdfNode>();
            System.Text.StringBuilder key = new System.Text.StringBuilder();
            for (int iz = 0; iz < cellsZ; ++iz)
            {
                for (int iy = 0; iy < cellsY; ++iy)
                {
                    for (int ix = 0; ix < cellsX; ++ix)
                    {
                        int index = (iz * cellsY + iy) * cellsX + ix;
                        if (!occupied[index])
                        {
                            continue;
                        }
                        SdfVector center = new SdfVector(lower.x + (ix + 0.5) * cellSize.x, lower.y + (iy + 0.5) * cellSize.y,
                                                         lower.z + (iz + 0.5) * cellSize.z);
                        SdfNode pruned = field.Prune(center, reach, out double least, out double most);
                        cellFields[index] = Shared(pruned, made, key);
                    }
                }
            }
            CompileCells(made, compile);
        }

        // The one field for all the cells whose cut-down fields are built alike
        private static SdfNode Shared(SdfNode pruned, Dictionary<string, SdfNode> made, System.Text.StringBuilder key)
        {
            key.Clear();
            pruned.Signature(key);
            string text = key.ToString();
            if (!made.TryGetValue(text, out SdfNode shared))
            {
                shared = pruned;
                made.Add(text, shared);
            }
            return shared;
        }

        // The fields of the cells are compiled once enough rays have hit them (CountHit): compiling them all took most of the
        // time that the cells take to set up, and paid for itself only in the cells that many rays cross
        private void CompileCells(Dictionary<string, SdfNode> made, bool compile)
        {
            prunedFields = made.Count;
            compiledCells = compile ? new System.Collections.Concurrent.ConcurrentDictionary<SdfNode, SdfNode>() : null;
            cellHits = compile ? new int[cellFields.Length] : null;
        }

        // The cells and their fields at once, from the top down (the hierarchy of Barbier et al., "Lipschitz pruning: hierarchical
        // simplification of primitive-based SDFs", 2025, section 3.5): the field is cut down for a block of cells, a block whose
        // values cannot reach 0 is left out with all its cells, and the others are split in two along every axis, each part
        // cut down from the field of the block. A part's ball lies inside the ball of its block, where the field of the block
        // has the values of the whole field. Neither the whole field nor the whole grid is gone through.
        private void PruneBlocks(int resolution, bool compile)
        {
            SizeCells(resolution);
            occupied = new bool[cellsX * cellsY * cellsZ];
            cellFields = new SdfNode[occupied.Length];
            pruneReach = 0.5 * cellSize.Length() * 1.001 + 4 * gradientStep;
            int size = 1;
            while (size < cellsX || size < cellsY || size < cellsZ)
            {
                size *= 2;
            }
            Dictionary<string, SdfNode> made = new Dictionary<string, SdfNode>();
            PruneBlock(field, 0, 0, 0, size, made, new System.Text.StringBuilder());
            CompileCells(made, compile);
        }

        // The block of size cells along every axis from the cell (x, y, z), of which node has the values
        private void PruneBlock(SdfNode node, int x, int y, int z, int size, Dictionary<string, SdfNode> made, System.Text.StringBuilder key)
        {
            if (x >= cellsX || y >= cellsY || z >= cellsZ)
            {
                return;
            }
            SdfVector center = new SdfVector(lower.x + (x + 0.5 * size) * cellSize.x, lower.y + (y + 0.5 * size) * cellSize.y,
                                             lower.z + (z + 0.5 * size) * cellSize.z);
            SdfNode pruned = node.Prune(center, 0.5 * size * cellSize.Length() * 1.001 + 4 * gradientStep, out double least, out double most);
            if (least > 0 || most < 0)
            {
                return;
            }
            // The range of a blend is wider than its values; the value at the center tells as much as it does for BuildCells
            if (Math.Abs(pruned.Evaluate(center)) > lipschitz * 0.5 * size * cellSize.Length() * (1 + 1e-9))
            {
                return;
            }
            if (size == 1)
            {
                int index = (z * cellsY + y) * cellsX + x;
                occupied[index] = true;
                cellFields[index] = Shared(pruned, made, key);
                return;
            }
            int half = size / 2;
            for (int k = 0; k < 8; ++k)
            {
                PruneBlock(pruned, x + (k & 1) * half, y + ((k >> 1) & 1) * half, z + ((k >> 2) & 1) * half, half, made, key);
            }
        }

        // The number of different cut-down fields of the cells
        public int PrunedFields()
        {
            return prunedFields;
        }

        // The cells of the box that the surface may cross: a cell whose value at the center is farther from 0 than the field can
        // change over half the diagonal of the cell holds no point of the surface
        private void SizeCells(int resolution)
        {
            SdfVector extent = upper - lower;
            double size = Math.Max(extent.x, Math.Max(extent.y, extent.z)) / resolution;
            cellsX = Math.Max(1, (int)Math.Ceiling(extent.x / size - 1e-9));
            cellsY = Math.Max(1, (int)Math.Ceiling(extent.y / size - 1e-9));
            cellsZ = Math.Max(1, (int)Math.Ceiling(extent.z / size - 1e-9));
            cellSize = new SdfVector(extent.x / cellsX, extent.y / cellsY, extent.z / cellsZ);
        }

        private void BuildCells(int resolution)
        {
            SizeCells(resolution);
            double reach = 0.5 * cellSize.Length();
            occupied = new bool[cellsX * cellsY * cellsZ];
            for (int iz = 0; iz < cellsZ; ++iz)
            {
                for (int iy = 0; iy < cellsY; ++iy)
                {
                    for (int ix = 0; ix < cellsX; ++ix)
                    {
                        SdfVector center = new SdfVector(lower.x + (ix + 0.5) * cellSize.x, lower.y + (iy + 0.5) * cellSize.y,
                                                         lower.z + (iz + 0.5) * cellSize.z);
                        double bound = localBounds ? Math.Min(field.Lipschitz(center, reach) * options.lipschitzFactor, lipschitz) : lipschitz;
                        occupied[(iz * cellsY + iy) * cellsX + ix] = Math.Abs(field.Evaluate(center)) <= bound * reach * (1 + 1e-9);
                    }
                }
            }
        }

        private bool MarchSpan(SdfVector o, SdfVector w, double u0, double u1, bool polish, out double uHit)
        {
            uHit = 0;
            double u = u0;
            double previousRadius = 0, stepLength = 0, omega = rigorousField ? options.relaxation : 1.0, previousU = u0;
            double side = FieldAt(o + w * u0).Evaluate(o + w * u0) < 0 ? -1 : 1;
            bool validate = !rigorousField && options.validation > 0;
            double previousSigned = 0;
            int plainSteps = 0;
            // The bound of the field along this stretch of the ray, where the field has one that depends on the direction
            double rayBound = options.rayBounds && rigorousField ?
                Math.Min(field.RayLipschitz(o + w * u0, o + w * u1) * options.lipschitzFactor, lipschitz) : lipschitz;
            for (int step = 0; step < options.maxSteps; ++step)
            {
                SdfVector p = o + w * u;
                double f = FieldAt(p).Evaluate(p);
                if (options.countEvaluations)
                {
                    ++marchEvaluations;
                }
                double distance = Math.Abs(f);
                if (validate && step > 0 && Math.Abs(stepLength) > 8 * epsilon &&
                    previousSigned - side * f > options.validation * Math.Abs(stepLength))
                {
                    // The estimate fell faster than a distance can along this step, so it overestimated the distance at the previous
                    // point: take half of the step instead (the previous point stays the reference)
                    stepLength *= 0.5;
                    u = previousU + stepLength;
                    omega = 1;
                    continue;
                }
                previousSigned = side * f;
                double signedRadius = side * f / Math.Min(StepBound(p, distance), rayBound);
                double radius = Math.Abs(signedRadius);
                if (omega > 1 && radius + previousRadius < stepLength)
                {
                    // The spheres do not overlap: go back to the safe step of the previous sphere and trace without relaxation
                    // (for the rest of the ray with the fixed factor, for the next step with the factor from the slope)
                    u = previousU + previousRadius;
                    stepLength = previousRadius;
                    if (options.slopeRelaxation)
                    {
                        plainSteps = 2;
                    }
                    else
                    {
                        omega = 1;
                    }
                    continue;
                }
                // (Quilez, "binary search for SDFs": a hit once the surface is nearer than the size of a pixel at this distance)
                if (distance <= Math.Max(epsilon, options.hitFootprint * u))
                {
                    uHit = u;
                    if (polish)
                    {
                        uHit = Polish(o, w, u, f);
                    }
                    return uHit <= u1 * (1 + 1e-12);
                }
                double factor = omega;
                if (options.slopeRelaxation && omega > 1)
                {
                    // The factor that makes the next ball touch this one if the distance keeps changing along the ray as it did
                    // over the last step (Balint and Valasek, "Accelerating sphere tracing", 2018): 2 / (1 - slope), a little
                    // short of it, and none right after a step that had to be taken back
                    factor = 1;
                    if (plainSteps > 0)
                    {
                        --plainSteps;
                    }
                    else if (step > 0 && stepLength > 0)
                    {
                        double slope = Math.Min((radius - previousRadius) / stepLength, 0.5);
                        factor = Math.Min(Math.Max(0.9 * 2 / (1 - slope), 1), options.relaxation);
                    }
                }
                previousU = u;
                previousRadius = radius;
                stepLength = factor * signedRadius;
                u += stepLength;
                if (u > u1)
                {
                    if (omega > 1 && stepLength > previousRadius)
                    {
                        // The relaxed step left the box; the unrelaxed one may still reach a surface inside it
                        u = previousU + previousRadius;
                        stepLength = previousRadius;
                        omega = 1;
                        if (u > u1)
                        {
                            return false;
                        }
                        continue;
                    }
                    return false;
                }
            }
            return false;
        }

        // The Lipschitz bound to divide the distance by at p: the global one, or for a field with local bounds the bound over a ball
        // that the step stays in (segment tracing). A step of radius r is safe when the bound over the ball of radius r is at most
        // distance / r. The bound over the ball of radius distance is one such bound (the step distance / bound is inside that ball);
        // a second try with the bound over the smaller ball is used when the larger step it allows passes its own test.
        private double StepBound(SdfVector p, double distance)
        {
            if (!localBounds || distance == 0)
            {
                return lipschitz;
            }
            double bound = Math.Min(LocalBound(p, distance) * options.lipschitzFactor, lipschitz);
            if (bound < 1)
            {
                // A step of distance / bound would leave the ball that the bound was found for
                return lipschitz;
            }
            double smaller = Math.Min(LocalBound(p, distance / bound) * options.lipschitzFactor, bound);
            if (smaller < bound && smaller >= 1)
            {
                double larger = Math.Min(LocalBound(p, distance / smaller) * options.lipschitzFactor, bound);
                if (larger <= smaller * (1 + 1e-12))
                {
                    return smaller;
                }
            }
            return bound;
        }

        // The Lipschitz bound of the field over the ball of the radius r about p: from the cut-down field of the cell of p when the
        // ball is inside the ball that the cell's field stands for the whole field in, which spares a walk of the whole tree
        private double LocalBound(SdfVector p, double r)
        {
            if (cellFields != null && r < pruneReach)
            {
                int ix = (int)Math.Floor((p.x - lower.x) / cellSize.x), iy = (int)Math.Floor((p.y - lower.y) / cellSize.y);
                int iz = (int)Math.Floor((p.z - lower.z) / cellSize.z);
                if (ix >= 0 && iy >= 0 && iz >= 0 && ix < cellsX && iy < cellsY && iz < cellsZ)
                {
                    SdfNode cell = cellFields[(iz * cellsY + iy) * cellsX + ix];
                    SdfVector center = new SdfVector(lower.x + (ix + 0.5) * cellSize.x, lower.y + (iy + 0.5) * cellSize.y,
                                                     lower.z + (iz + 0.5) * cellSize.z);
                    if (cell != null && (p - center).Length() + r <= pruneReach)
                    {
                        return cell.Lipschitz(p, r);
                    }
                }
            }
            return field.Lipschitz(p, r);
        }

        // Two Newton steps along the ray from a point that is already within the tolerance of the surface
        private double Polish(SdfVector o, SdfVector w, double u, double f)
        {
            for (int i = 0; i < 2; ++i)
            {
                SdfVector p = o + w * u;
                SdfVector g = FieldAt(p).Gradient(p, gradientStep, options.analyticGradient);
                double slope = SdfVector.Dot(g, w);
                if (Math.Abs(slope) < 0.05 * g.Length() || g.Length() == 0)
                {
                    break;
                }
                double next = u - f / slope;
                if (Math.Abs(next - u) > 100 * epsilon)
                {
                    break;
                }
                u = next;
                f = FieldAt(o + w * u).Evaluate(o + w * u);
            }
            return u;
        }

        // The ray in object space: origin, unit direction, the length of the transformed direction, and the box interval in distance
        private bool PrepareRay(Ray ray, Float tMax, out SdfVector o, out SdfVector w, out double dirLength, out double u0,
                                out double u1)
        {
            double[] m = objectFromRenderMatrix;
            double ox = ray.o.x, oy = ray.o.y, oz = ray.o.z, dx = ray.d.x, dy = ray.d.y, dz = ray.d.z;
            o = new SdfVector(m[0] * ox + m[1] * oy + m[2] * oz + m[3], m[4] * ox + m[5] * oy + m[6] * oz + m[7],
                              m[8] * ox + m[9] * oy + m[10] * oz + m[11]);
            SdfVector d = new SdfVector(m[0] * dx + m[1] * dy + m[2] * dz, m[4] * dx + m[5] * dy + m[6] * dz,
                                        m[8] * dx + m[9] * dy + m[10] * dz);
            dirLength = d.Length();
            w = d / dirLength;
            u0 = 0;
            u1 = double.IsInfinity(tMax) ? double.MaxValue : tMax * dirLength;
            for (int axis = 0; axis < 3; ++axis)
            {
                double near, far;
                if (w[axis] == 0)
                {
                    if (o[axis] < lower[axis] || o[axis] > upper[axis])
                    {
                        return false;
                    }
                    continue;
                }
                near = (lower[axis] - o[axis]) / w[axis];
                far = (upper[axis] - o[axis]) / w[axis];
                if (near > far)
                {
                    double swap = near;
                    near = far;
                    far = swap;
                }
                u0 = Math.Max(u0, near);
                u1 = Math.Min(u1, far * (1 + 1e-12));
                if (u0 > u1)
                {
                    return false;
                }
            }
            return true;
        }

        public override bool IntersectP(Ray ray, Float tMax = Infinity)
        {
            SdfTime.Current = ray.time;
            if (!PrepareRay(ray, tMax, out SdfVector o, out SdfVector w, out double dirLength, out double u0, out double u1))
            {
                return false;
            }
            if (!March(o, w, u0, u1, false, out double u))
            {
                return false;
            }
            return u / dirLength > 0 && u / dirLength <= tMax;
        }

        public override pstd.optional<ShapeIntersection> Intersect(Ray ray, Float tMax = Infinity)
        {
            SdfTime.Current = ray.time;
            if (!PrepareRay(ray, tMax, out SdfVector o, out SdfVector w, out double dirLength, out double u0, out double u1))
            {
                return default;
            }
            if (!March(o, w, u0, u1, true, out double u))
            {
                return default;
            }
            double tHit = u / dirLength;
            if (!(tHit > 0) || tHit > tMax)
            {
                return default;
            }
            SdfVector pObject = o + w * u;
            // The offset of the normal's central difference grows with the distance along the ray when a footprint is given
            // (Quilez, "normals for an SDF": an offset of the size of the pixel filters the detail that the pixel cannot show)
            SurfaceInteraction intr = InteractionFromPoint(pObject, objectFromRender.Apply(-ray.d), ray.time,
                                                           Math.Max(gradientStep, options.normalFootprint * u));
            return new ShapeIntersection(intr, (Float)tHit);
        }

        // The unit outward normal (the normalized gradient) at an object space point, from the field of the cell of the point:
        // the whole field has the same values, at the cost of all its parts (a hit asks for four normals around it and
        // three values besides the normal itself, which with the whole field took four times as long as finding the hit)
        private SdfVector NormalAt(SdfVector p)
        {
            return FieldAt(p).Gradient(p, gradientStep, options.analyticGradient).Normalized();
        }

        // The mapping from the position to (u, v), with the gradients of u and v with respect to the position
        private void UvMapping(SdfVector p, SdfVector n, out Point2f uv, out SdfVector gradU, out SdfVector gradV)
        {
            double twoPi = 2 * Math.PI;
            double rho2 = p.x * p.x + p.y * p.y;
            double rho = Math.Sqrt(rho2);
            double rho2Safe = Math.Max(rho2, 1e-30);
            double phi = Math.Atan2(p.y, p.x);
            if (phi < 0)
            {
                phi += twoPi;
            }
            SdfVector gradPhi = new SdfVector(-p.y / rho2Safe, p.x / rho2Safe, 0);
            SdfUvMapping mode = options.uvMapping;
            if (mode == SdfUvMapping.Box)
            {
                int axis = Math.Abs(n.x) >= Math.Abs(n.y) && Math.Abs(n.x) >= Math.Abs(n.z) ? 0 : (Math.Abs(n.y) >= Math.Abs(n.z) ? 1 : 2);
                if (axis == 0)
                {
                    uv = new Point2f((Float)p.y, (Float)p.z);
                    gradU = new SdfVector(0, 1, 0);
                    gradV = new SdfVector(0, 0, 1);
                }
                else if (axis == 1)
                {
                    uv = new Point2f((Float)p.x, (Float)p.z);
                    gradU = new SdfVector(1, 0, 0);
                    gradV = new SdfVector(0, 0, 1);
                }
                else
                {
                    uv = new Point2f((Float)p.x, (Float)p.y);
                    gradU = new SdfVector(1, 0, 0);
                    gradV = new SdfVector(0, 1, 0);
                }
                return;
            }
            if (mode == SdfUvMapping.Planar)
            {
                uv = new Point2f((Float)p.x, (Float)p.y);
                gradU = new SdfVector(1, 0, 0);
                gradV = new SdfVector(0, 1, 0);
                return;
            }
            if (mode == SdfUvMapping.PlanarYZ)
            {
                uv = new Point2f((Float)p.y, (Float)p.z);
                gradU = new SdfVector(0, 1, 0);
                gradV = new SdfVector(0, 0, 1);
                return;
            }
            if (mode == SdfUvMapping.PlanarXZ)
            {
                uv = new Point2f((Float)p.x, (Float)p.z);
                gradU = new SdfVector(1, 0, 0);
                gradV = new SdfVector(0, 0, 1);
                return;
            }
            if (mode == SdfUvMapping.Cylindrical)
            {
                uv = new Point2f((Float)(phi / twoPi), (Float)p.z);
                gradU = gradPhi / twoPi;
                gradV = new SdfVector(0, 0, 1);
                return;
            }
            double r = p.Length();
            double r2 = Math.Max(r * r, 1e-30);
            double theta = Math.Acos(Math.Min(Math.Max(p.z / Math.Max(r, 1e-30), -1), 1));
            uv = new Point2f((Float)(phi / twoPi), (Float)(theta / Math.PI));
            gradU = gradPhi / twoPi;
            double rhoSafe = Math.Max(rho, 1e-15);
            SdfVector gradTheta = new SdfVector(p.x * p.z / (r2 * rhoSafe), p.y * p.z / (r2 * rhoSafe), -rho / r2);
            gradV = gradTheta / Math.PI;
        }

        private SurfaceInteraction InteractionFromPoint(SdfVector pObject, Vector3f woObject, Float time)
        {
            return InteractionFromPoint(pObject, woObject, time, gradientStep);
        }

        private SurfaceInteraction InteractionFromPoint(SdfVector pObject, Vector3f woObject, Float time, double normalStep)
        {
            if (compiledCells != null)
            {
                CountHit(pObject);
            }
            SdfVector n = FieldAt(pObject).Gradient(pObject, normalStep, options.analyticGradient).Normalized();
            UvMapping(pObject, n, out Point2f uv, out SdfVector gradU, out SdfVector gradV);

            // The tangent vectors dual to the gradients of u and v, restricted to the tangent plane
            SdfVector tangentU = gradU - n * SdfVector.Dot(gradU, n);
            SdfVector tangentV = gradV - n * SdfVector.Dot(gradV, n);
            double g00 = SdfVector.Dot(tangentU, tangentU), g01 = SdfVector.Dot(tangentU, tangentV),
                   g11 = SdfVector.Dot(tangentV, tangentV);
            double determinant = g00 * g11 - g01 * g01;
            SdfVector dpdu, dpdv;
            if (determinant > 1e-24 * g00 * g11 && determinant > 0)
            {
                dpdu = tangentU * (g11 / determinant) - tangentV * (g01 / determinant);
                dpdv = tangentV * (g00 / determinant) - tangentU * (g01 / determinant);
            }
            else
            {
                Vector3f nf = new Vector3f((Float)n.x, (Float)n.y, (Float)n.z);
                Vector3f t1, t2;
                pbrt.CoordinateSystem(nf, &t1, &t2);
                dpdu = new SdfVector(t1.x, t1.y, t1.z);
                dpdv = new SdfVector(t2.x, t2.y, t2.z);
            }

            // The change of the normal along the tangent vectors
            SdfVector dndu = NormalDerivative(pObject, dpdu), dndv = NormalDerivative(pObject, dpdv);

            // The residual of the field bounds the distance of the point to the true surface
            double residual = Math.Abs(FieldAt(pObject).Evaluate(pObject)) / Math.Max(lipschitz, 1e-300);
            Vector3f pError = pbrt.gamma(5) * pbrt.Abs(new Vector3f((Float)pObject.x, (Float)pObject.y, (Float)pObject.z)) +
                              new Vector3f((Float)residual, (Float)residual, (Float)residual);

            bool flipNormal = reverseOrientation ^ transformSwapsHandedness;
            SurfaceInteraction si = new SurfaceInteraction(
                new Point3fi(new Point3f((Float)pObject.x, (Float)pObject.y, (Float)pObject.z), pError), uv, woObject,
                ToVector(dpdu), ToVector(dpdv), new Normal3f((Float)dndu.x, (Float)dndu.y, (Float)dndu.z),
                new Normal3f((Float)dndv.x, (Float)dndv.y, (Float)dndv.z), time, flipNormal);
            // The geometric normal is the outward gradient, not the cross product of the tangents (their order depends on the mapping)
            Normal3f normal = new Normal3f((Float)n.x, (Float)n.y, (Float)n.z);
            if (flipNormal)
            {
                normal *= -1;
            }
            si.n = normal;
            si.shading.n = normal;
            return renderFromObject.Apply(si);
        }

        private static Vector3f ToVector(SdfVector v)
        {
            return new Vector3f((Float)v.x, (Float)v.y, (Float)v.z);
        }

        // The derivative of the unit normal along a tangent vector, by a central difference of normals a fixed distance away
        private SdfVector NormalDerivative(SdfVector p, SdfVector tangent)
        {
            double length = tangent.Length();
            if (length == 0)
            {
                return new SdfVector(0, 0, 0);
            }
            double distance = 1e-3 * scale;
            SdfVector move = tangent * (distance / length);
            SdfVector difference = NormalAt(p + move) - NormalAt(p - move);
            return difference * (length / (2 * distance));
        }

        // Sampling by area (see SdfSurfaceSampler)

        private SdfSurfaceSampler Sampler()
        {
            if (sampler == null)
            {
                lock (samplerLock)
                {
                    if (sampler == null)
                    {
                        sampler = new SdfSurfaceSampler(field, lipschitz, lower, upper, renderFromObjectMatrix,
                                                        options.tessellation, options.areaSubdivisions, gradientStep,
                                                        options.analyticGradient);
                    }
                }
            }
            return sampler;
        }

        public override Float Area()
        {
            return (Float)Sampler().Area();
        }

        // The interaction at a point of the surface (object space)
        private Interaction SurfacePoint(SdfVector pObject)
        {
            SdfVector n = NormalAt(pObject);
            Normal3f nObject = new Normal3f((Float)n.x, (Float)n.y, (Float)n.z);
            Normal3f normal = pbrt.Normalize(renderFromObject.Apply(nObject));
            if (reverseOrientation ^ transformSwapsHandedness)
            {
                normal *= -1;
            }
            UvMapping(pObject, n, out Point2f uv, out SdfVector gradU, out SdfVector gradV);
            double residual = Math.Abs(FieldAt(pObject).Evaluate(pObject)) / Math.Max(lipschitz, 1e-300);
            Vector3f pError = pbrt.gamma(5) * pbrt.Abs(new Vector3f((Float)pObject.x, (Float)pObject.y, (Float)pObject.z)) +
                              new Vector3f((Float)residual, (Float)residual, (Float)residual);
            Point3fi pi = renderFromObject.Apply(
                new Point3fi(new Point3f((Float)pObject.x, (Float)pObject.y, (Float)pObject.z), pError));
            return new Interaction(pi, normal, uv);
        }

        public override pstd.optional<ShapeSample> Sample(Point2f u)
        {
            SdfSurfaceSampler surface = Sampler();
            SdfVector pObject = surface.Sample(u[0], u[1]);
            return new ShapeSample(SurfacePoint(pObject), 1 / (Float)surface.Area());
        }

        public override Float PDF(Interaction intr)
        {
            return 1 / Area();
        }

        public override pstd.optional<ShapeSample> Sample(ShapeSampleContext ctx, Point2f u)
        {
            // Sample by area (uniformly, or by the importance of the triangles for the receiving point) and convert the density to
            // solid angle
            pstd.optional<ShapeSample> ss;
            if (options.importanceSampling)
            {
                SdfSurfaceSampler surface = Sampler();
                SdfVector pObject = surface.SampleFrom(new SdfVector(ctx.p().x, ctx.p().y, ctx.p().z), u[0], u[1], out double areaDensity);
                if (areaDensity <= 0)
                {
                    return default;
                }
                ss = new ShapeSample(SurfacePoint(pObject), (Float)areaDensity);
            }
            else
            {
                ss = Sample(u);
            }
            if (!ss)
            {
                return default;
            }
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

        public override Float PDF(ShapeSampleContext ctx, Vector3f wi)
        {
            // Intersect the sample ray with the surface and convert the density per area of the first hit to solid angle
            Ray ray = ctx.SpawnRay(wi);
            pstd.optional<ShapeIntersection> isect = Intersect(ray);
            if (!isect)
            {
                return 0;
            }
            Float areaDensity = 1 / Area();
            if (options.importanceSampling)
            {
                Point3f receiver = ctx.p(), hit = isect.value().intr.p();
                areaDensity = (Float)Sampler().DensityFrom(new SdfVector(receiver.x, receiver.y, receiver.z),
                                                           new SdfVector(hit.x, hit.y, hit.z));
            }
            Float pdf = areaDensity / (pbrt.AbsDot(isect.value().intr.n, -wi) /
                                        pbrt.DistanceSquared(ctx.p(), isect.value().intr.p()));
            if (pbrt.IsInf(pdf))
            {
                pdf = 0;
            }
            return pdf;
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ SdfShape renderFromObject: %s reverseOrientation: %s lipschitz: %f epsilon: %f ]",
                                     renderFromObject, reverseOrientation, (Float)lipschitz, (Float)epsilon);
        }

        // Creates the shape from its parameters
        public static SdfShape Create(Transform renderFromObject, Transform objectFromRender, bool reverseOrientation,
                                      ParameterDictionary parameters, FileLoc loc, Allocator alloc)
        {
            string expression = parameters.GetOneString("sdf", "");
            if (expression.Length == 0)
            {
                pbrt.ErrorExit(loc, "sdf: the \"string sdf\" expression is required.");
            }
            Point3f lower = parameters.GetOnePoint3f("pmin", new Point3f(-1.5f, -1.5f, -1.5f));
            Point3f upper = parameters.GetOnePoint3f("pmax", new Point3f(1.5f, 1.5f, 1.5f));
            // Without a box the field is asked for its own (SdfNode.Bounds), by a first parse whose only use is the box; a field
            // that does not know one (a plane, a fractal, a mesh) keeps the default box
            bool boxGiven = parameters.GetPoint3fArray("pmin").Count > 0 || parameters.GetPoint3fArray("pmax").Count > 0;
            bool boxKnown = boxGiven;
            // Sphere Carving (sdfcarving.cs) needs a field that does not move with time
            bool carvable = !SdfCarving.MovesWithTime(expression);
            // The field is parsed more than once (for its box, for a carving, for the tracing): a mesh or a grid is loaded once
            Dictionary<string, SdfNode> loaded = new Dictionary<string, SdfNode>();
            Func<SdfParseContext> newContext = () =>
            {
                SdfParseContext made = new SdfParseContext();
                made.legacyPhases = parameters.GetOneBool("legacyphases", false);
                made.meshLoader = (string file, int resolution, double padding, string sign) =>
                {
                    string key = "mesh|" + file + "|" + resolution + "|" + padding + "|" + sign;
                    if (!loaded.TryGetValue(key, out SdfNode node))
                    {
                        node = SdfMeshLoader.Load(file, resolution, padding, sign);
                        loaded.Add(key, node);
                    }
                    return node;
                };
                made.vdbLoader = (string file, string grid) =>
                {
                    string key = "vdb|" + file + "|" + grid;
                    if (!loaded.TryGetValue(key, out SdfNode node))
                    {
                        node = SdfVdbLoader.Load(file, grid);
                        loaded.Add(key, node);
                    }
                    return node;
                };
                return made;
            };
            if (!boxGiven)
            {
                SdfParseContext probe = newContext();
                probe.domainRadius = 1;
                probe.quiet = true;
                if (SdfParser.Parse(expression, probe).Bounds(out SdfVector fieldLower, out SdfVector fieldUpper, out bool grows))
                {
                    // A margin keeps the surfaces that lie in a face of the box inside it
                    double margin = 0.02 * Math.Max(fieldUpper.x - fieldLower.x, Math.Max(fieldUpper.y - fieldLower.y, fieldUpper.z - fieldLower.z)) + 1e-6;
                    lower = new Point3f((Float)(fieldLower.x - margin), (Float)(fieldLower.y - margin), (Float)(fieldLower.z - margin));
                    upper = new Point3f((Float)(fieldUpper.x + margin), (Float)(fieldUpper.y + margin), (Float)(fieldUpper.z + margin));
                    boxKnown = true;
                }
            }
            int carveSpheres = parameters.GetOneInt("carvespheres", 4000), carvePlanes = parameters.GetOneInt("carveplanes", 20);
            double carveThreshold = parameters.GetOneFloat("carvethreshold", 0.05f), carveRadius = parameters.GetOneFloat("carveradius", 10);
            if (!boxKnown)
            {
                // A field that does not know its extent is carved from a large sphere about the origin for its box
                SdfCarving found = carvable ? SdfCarving.FromExpression(expression, newContext, new SdfVector(0, 0, 0), carveRadius, 1,
                                                                        0.01 * carveRadius, carveSpheres) : null;
                if (found != null)
                {
                    found.Bounds(out SdfVector fieldLower, out SdfVector fieldUpper);
                    double margin = 0.02 * Math.Max(fieldUpper.x - fieldLower.x, Math.Max(fieldUpper.y - fieldLower.y, fieldUpper.z - fieldLower.z)) + 1e-6;
                    lower = new Point3f((Float)(fieldLower.x - margin), (Float)(fieldLower.y - margin), (Float)(fieldLower.z - margin));
                    upper = new Point3f((Float)(fieldUpper.x + margin), (Float)(fieldUpper.y + margin), (Float)(fieldUpper.z + margin));
                    boxKnown = true;
                }
                else
                {
                    pbrt.Warning(loc, pbrt.StringPrintf("sdf: the field does not know its own extent, carving a box for it from the sphere of " +
                                                        "radius %f did not work (the surface reaches that sphere, or the field is a fractal, a " +
                                                        "mesh or moves with time), and no \"pmin\" and \"pmax\" are given: the box from -1.5 " +
                                                        "to 1.5 is used.", carveRadius));
                }
            }
            double[][] carvedHulls = null;
            double carvedEpsilon = 0;
            if (parameters.GetOneBool("carve", false))
            {
                SdfVector boxCenter = new SdfVector(0.5 * (lower.x + upper.x), 0.5 * (lower.y + upper.y), 0.5 * (lower.z + upper.z));
                double reach = 0.5 * Math.Sqrt((upper.x - lower.x) * (upper.x - lower.x) + (upper.y - lower.y) * (upper.y - lower.y) +
                                               (upper.z - lower.z) * (upper.z - lower.z));
                // V0 far away for an exact distance (twelve points cover it), closer for the small balls of a looser bound
                SdfCarving carving = carvable ? SdfCarving.FromExpression(expression, newContext, boxCenter, 4 * reach, 1.5 / 4,
                                                                          carveThreshold * reach, carveSpheres) : null;
                if (carving != null)
                {
                    carvedHulls = SdfCarvedHulls.FromCarving(carving, Math.Max(1, parameters.GetOneInt("carvehulls", 1)), carvePlanes);
                    carvedEpsilon = 0.01 * reach;
                    // The box of the carved points, within the box so far
                    carving.Bounds(out SdfVector carvedLower, out SdfVector carvedUpper);
                    double margin = 0.01 * reach + 1e-6;
                    Point3f tightLower = new Point3f((Float)Math.Max(lower.x, carvedLower.x - margin), (Float)Math.Max(lower.y, carvedLower.y - margin),
                                                     (Float)Math.Max(lower.z, carvedLower.z - margin));
                    Point3f tightUpper = new Point3f((Float)Math.Min(upper.x, carvedUpper.x + margin), (Float)Math.Min(upper.y, carvedUpper.y + margin),
                                                     (Float)Math.Min(upper.z, carvedUpper.z + margin));
                    if (tightLower.x < tightUpper.x && tightLower.y < tightUpper.y && tightLower.z < tightUpper.z)
                    {
                        lower = tightLower;
                        upper = tightUpper;
                    }
                }
                else
                {
                    pbrt.Warning(loc, "sdf: \"carve\" did not work for this field (the surface reaches out of its box, or the field is a " +
                                      "fractal, a mesh or moves with time): it is traced without the carved bound.");
                }
            }
            if (lower.x >= upper.x || lower.y >= upper.y || lower.z >= upper.z)
            {
                pbrt.ErrorExit(loc, "sdf: \"pmin\" must be below \"pmax\" in every coordinate.");
            }
            SdfShapeOptions options = new SdfShapeOptions();
            options.epsilon = parameters.GetOneFloat("epsilon", (Float)options.epsilon);
            options.maxSteps = parameters.GetOneInt("maxsteps", options.maxSteps);
            options.relaxation = Math.Max(1.0, parameters.GetOneFloat("relaxation", (Float)options.relaxation));
            options.validation = Math.Max(0.0, parameters.GetOneFloat("validation", (Float)options.validation));
            options.lipschitzFactor = parameters.GetOneFloat("lipschitz", 1);
            options.tessellation = parameters.GetOneInt("tessellation", options.tessellation);
            options.areaSubdivisions = parameters.GetOneInt("areasubdivisions", options.areaSubdivisions);
            options.importanceSampling = parameters.GetOneBool("importance", options.importanceSampling);
            options.analyticGradient = parameters.GetOneBool("analyticgradient", options.analyticGradient);
            options.normalFootprint = Math.Max(0.0, parameters.GetOneFloat("normalfootprint", 0));
            options.hitFootprint = Math.Max(0.0, parameters.GetOneFloat("hitfootprint", 0));
            options.ddaResolution = Math.Max(0, parameters.GetOneInt("dda", 0));
            options.prune = parameters.GetOneBool("prune", false);
            options.slopeRelaxation = parameters.GetOneBool("sloperelaxation", false);
            options.rayBounds = parameters.GetOneBool("raybounds", false);
            options.pruneHierarchy = parameters.GetOneBool("prunehierarchy", true);
            options.pruneCompileHits = parameters.GetOneInt("prunecompilehits", 16);
            options.pruneCompile = parameters.GetOneBool("compile", true) && parameters.GetOneBool("prunecompile", true);
            if (options.ddaResolution > 0 && SdfCarving.MovesWithTime(expression))
            {
                pbrt.Warning(loc, "sdf: \"dda\" is not used for a field that moves with time.");
                options.ddaResolution = 0;
            }
            string mapping = parameters.GetOneString("uvmapping", "spherical");
            if (mapping == "spherical")
            {
                options.uvMapping = SdfUvMapping.Spherical;
            }
            else if (mapping == "cylindrical")
            {
                options.uvMapping = SdfUvMapping.Cylindrical;
            }
            else if (mapping == "planar")
            {
                options.uvMapping = SdfUvMapping.Planar;
            }
            else if (mapping == "box" || mapping == "triplanar")
            {
                // The face is chosen by the dominant axis of the normal (a blend of three lookups is the "triplanar" texture)
                options.uvMapping = SdfUvMapping.Box;
            }
            else if (mapping == "planaryz" || mapping == "yz")
            {
                options.uvMapping = SdfUvMapping.PlanarYZ;
            }
            else if (mapping == "planarxz" || mapping == "xz")
            {
                options.uvMapping = SdfUvMapping.PlanarXZ;
            }
            else
            {
                pbrt.ErrorExit(loc, pbrt.StringPrintf("%s: unknown sdf \"uvmapping\".", mapping));
            }
            SdfParseContext context = newContext();
            context.domainRadius = Math.Sqrt(Math.Max(lower.x * lower.x, upper.x * upper.x) +
                                             Math.Max(lower.y * lower.y, upper.y * upper.y) +
                                             Math.Max(lower.z * lower.z, upper.z * upper.z));
            SdfLocalBounds.warps = parameters.GetOneBool("warpbounds", false);
            SdfNode field = SdfParser.Parse(expression, context);
            SdfLocalBounds.warps = false;
            int proxyNodes = parameters.GetOneInt("proxies", 0);
            if (proxyNodes > 0)
            {
                field = field.WithProxies(proxyNodes, 0);
            }
            if (parameters.GetOneBool("compile", true))
            {
                field = SdfCompiledNode.Compile(field, parameters.GetOneBool("cull", false));
            }
            if (carvedHulls != null)
            {
                string use = parameters.GetOneString("carveuse", "clip");
                if (use == "proxy")
                {
                    field = new SdfCarvedField(field, carvedHulls, carvedEpsilon);
                }
                else if (use == "clip")
                {
                    // A margin as for the box: a carved point may lie on the surface
                    options.clipHulls = carvedHulls;
                    for (int h = 0; h < carvedHulls.Length; ++h)
                    {
                        for (int i = 3; i < carvedHulls[h].Length; i += 4)
                        {
                            carvedHulls[h][i] += carvedEpsilon;
                        }
                    }
                }
                else
                {
                    pbrt.ErrorExit(loc, pbrt.StringPrintf("%s: unknown sdf \"carveuse\" (\"proxy\" or \"clip\").", use));
                }
            }
            return new SdfShape(renderFromObject, objectFromRender, reverseOrientation, field, lower, upper, options);
        }

        // SdfShape Private Members
        private readonly Transform renderFromObject, objectFromRender;
        private readonly bool reverseOrientation, transformSwapsHandedness;
        private readonly SdfNode field;
        private readonly SdfShapeOptions options;
        private readonly SdfVector lower, upper;
        private bool[] occupied;
        private SdfNode[] cellFields;
        private int[] cellHits;
        private System.Collections.Concurrent.ConcurrentDictionary<SdfNode, SdfNode> compiledCells;
        private int prunedFields;
        // The values of the field asked for by the marching so far (a count for tests and timings; not exact across threads)
        public long marchEvaluations;
        private double pruneReach;
        private int cellsX, cellsY, cellsZ;
        private SdfVector cellSize;
        private readonly double lipschitz, scale, epsilon, gradientStep;
        private readonly bool localBounds, rigorousField;
        private readonly double[] objectFromRenderMatrix, renderFromObjectMatrix;
        private SdfSurfaceSampler sampler;
        private readonly object samplerLock = new object();
    }

}  // namespace pbrt
