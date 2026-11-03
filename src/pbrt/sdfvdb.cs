// (not in pbrt-v4) sdfvdb.cs
//
// Level set grids of OpenVDB (as NanoVDB files) as signed distance fields: vdb("file.nvdb", "gridname") in an sdf expression. A level
// set stores the signed distance (negative inside) in a narrow band of voxels around the surface; the field here reads it with the
// trilinear reconstruction of NanoVDB, and continues it beyond the band: where the grid has no value the sample is the background (the
// half width of the band, a lower bound of the distance outside, and its negative inside), and beyond the bounding box of the
// active voxels the distance to the box is added to the value at its border, as SdfGrid does. The values of a level set are distances
// in world units, so the Lipschitz bound is that of a distance field reconstructed by trilinear interpolation.

using System;

namespace pbrt
{

    // SdfVdbGrid Definition
    public sealed unsafe class SdfVdbGrid : SdfNode
    {
        // SdfVdbGrid Public Methods
        public SdfVdbGrid(nanovdb.GridHandle<NanoVDBBuffer> handle)
        {
            // The handle owns the memory of the grid
            this.handle = handle;
            grid = handle.grid<float>();
            background = grid.tree().background();
            nanovdb.BBox<double> box = grid.worldBBox();
            lower = new SdfVector(box.min()[0], box.min()[1], box.min()[2]);
            upper = new SdfVector(box.max()[0], box.max()[1], box.max()[2]);
        }

        // The bounds of the active voxels in world space
        public void WorldBounds(out SdfVector lowerBound, out SdfVector upperBound)
        {
            lowerBound = lower;
            upperBound = upper;
        }

        public double VoxelSize()
        {
            return grid.voxelSize()[0];
        }

        public override double Evaluate(SdfVector p)
        {
            double cx = Math.Min(Math.Max(p.x, lower.x), upper.x);
            double cy = Math.Min(Math.Max(p.y, lower.y), upper.y);
            double cz = Math.Min(Math.Max(p.z, lower.z), upper.z);
            double outside = new SdfVector(p.x - cx, p.y - cy, p.z - cz).Length();
            nanovdb.Vec3<double> index = grid.worldToIndexF(new nanovdb.Vec3<double>(cx, cy, cz));
            double value = new nanovdb.SampleFromVoxels<nanovdb.NanoTree<float>, float>(grid.tree()).Apply(index);
            return value + outside;
        }

        // Trilinear interpolation of the distances of a level set can overshoot the distance a little
        public override double Lipschitz()
        {
            return 1.25;
        }

        // SdfVdbGrid Private Members
        private readonly nanovdb.GridHandle<NanoVDBBuffer> handle;
        private readonly nanovdb.NanoGrid<float> grid;
        private readonly float background;
        private readonly SdfVector lower, upper;
    }

    // SdfVdbLoader Definition
    public static class SdfVdbLoader
    {
        // Reads a grid of a NanoVDB file (the first grid for an empty name) and makes its signed distance field
        public static SdfNode Load(string filename, string gridName)
        {
            NanoVDBBuffer buffer = new NanoVDBBuffer(null);
            nanovdb.GridHandle<NanoVDBBuffer> handle = new nanovdb.GridHandle<NanoVDBBuffer>();
            try
            {
                string resolved = pbrt.ResolveFilename(filename);
                handle = gridName.Length > 0 ? nanovdb.io.IO.readGrid<NanoVDBBuffer>(resolved, gridName, 0, buffer)
                                             : nanovdb.io.IO.readGrid<NanoVDBBuffer>(resolved, 0, 0, buffer);
            }
            catch (Exception e) when (!(e is FatalException))
            {
                pbrt.ErrorExit(filename + ": " + e.Message);
            }
            if (!handle)
            {
                pbrt.ErrorExit(filename + ": no grid" + (gridName.Length > 0 ? " named \"" + gridName + "\"" : "") + " in the file.");
            }
            if (handle.grid<float>().IsNull)
            {
                pbrt.ErrorExit(filename + ": the grid isn't a float grid.");
            }
            if (!handle.gridMetaData().isLevelSet() && !handle.gridMetaData().isUnknown())
            {
                pbrt.ErrorExit(filename + ": the grid isn't a level set (it is a fog volume or another class of grid).");
            }
            return new SdfVdbGrid(handle);
        }
    }

}  // namespace pbrt
