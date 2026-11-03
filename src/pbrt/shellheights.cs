// (not in pbrt-v4) shellheights.cs
//
// The displacement maps of the "displacedmesh" shape, as normalized heights on a grid of points with a min-max pyramid over the cells
// (a cell is the square between four neighbouring points; the surface of a cell is two micro triangles over its corners):
//
//  * ShellHeightField: a grid of heights, stored as floats, as 16 bit fixed point numbers, or as 16 bit numbers packed in blocks with
//    a plane predictor and residuals of as few bits as the block needs (the predict-and-correct idea of the displaced micro-meshes).
//    With the compact storages the pyramid keeps no level for the cells (it is computed from the four corners) and 16 bit numbers for
//    the other levels, so the memory per height drops from 14.7 bytes to between 3.3 and about 2 bytes. Nodes of the pyramid in which
//    the heights are a plane (within a tolerance) are flagged, and the traversal intersects such a node with two triangles over its
//    corners without descending (the adaptive subdivision of micro-meshes, where flat areas need fewer micro triangles).
//  * Rectangular queries (Thonat et al., "RMIP", SIGGRAPH Asia 2023): the minimum and the maximum over any axis-aligned rectangle of the
//    map from four precomputed windows of power of two sizes (Eq. 10), at a reduced resolution as RMIP does for memory.
//  * ShellProceduralField: a function of (u, v) with no stored heights; the range over a node comes from interval arithmetic.

using System;
using System.Collections.Generic;

namespace pbrt
{

    // ShellStorage Definition
    public enum ShellStorage
    {
        Float32,
        Fixed16,
        Packed
    }

    // ShellHeights Definition: what the traversal needs from a displacement map
    public abstract class ShellHeights
    {
        // ShellHeights Public Methods
        protected ShellHeights(int cellsX, int cellsY)
        {
            this.cellsX = cellsX;
            this.cellsY = cellsY;
            int levelCount = 1;
            int sizeX = cellsX, sizeY = cellsY;
            while (sizeX > 1 || sizeY > 1)
            {
                sizeX = (sizeX + 1) / 2;
                sizeY = (sizeY + 1) / 2;
                ++levelCount;
            }
            levels = levelCount;
        }

        // The height at a grid point
        public abstract double Sample(int x, int y);

        // The range of the heights of the surface over the node (i, j) of a level (level 0: a cell, level k: 2^k x 2^k cells)
        public abstract void NodeRange(int level, int i, int j, out double low, out double high);

        // The range of the heights of the surface over the cells that the rectangle [x0, x1] x [y0, y1] (in grid units) touches;
        // false when it touches none
        public abstract bool RectangleRange(double x0, double y0, double x1, double y1, out double low, out double high);

        // True when the heights over the node are within a tolerance of the two triangles over its four corners
        public virtual bool IsFlat(int level, int i, int j)
        {
            return false;
        }

        public int LevelWidth(int level)
        {
            return ((cellsX - 1) >> level) + 1;
        }

        public int LevelHeight(int level)
        {
            return ((cellsY - 1) >> level) + 1;
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

        // The range of the heights over the cells that a triangle of the grid plane touches (corners in grid units); false when it
        // touches none. The cells that the triangle contains as a whole are covered by the nodes of the pyramid, so the work is
        // proportional to the boundary of the triangle.
        public bool TriangleRange(double ax, double ay, double bx, double by, double cx, double cy, out double low, out double high)
        {
            low = double.MaxValue;
            high = double.MinValue;
            double area = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
            double orientation = area >= 0 ? 1 : -1;
            double[] corners = { ax, ay, bx, by, cx, cy };
            // Nodes that the boundary of the triangle crosses are split down to cells, unless that takes more than a couple of
            // thousand of them (a huge grid): then they are taken whole at the level where it does, which only widens the range
            double perimeter = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay)) + Math.Sqrt((cx - bx) * (cx - bx) + (cy - by) * (cy - by)) +
                               Math.Sqrt((ax - cx) * (ax - cx) + (ay - cy) * (ay - cy));
            int smallestLevel = 0;
            while (perimeter / (1 << smallestLevel) > 2048 && smallestLevel < levels - 1)
            {
                ++smallestLevel;
            }
            return TriangleNode(levels - 1, 0, 0, corners, orientation, Math.Abs(area) < 1e-12, smallestLevel, ref low, ref high);
        }

        // ShellHeights Public Members
        public readonly int cellsX, cellsY, levels;

        // ShellHeights Private Methods
        private bool TriangleNode(int level, int i, int j, double[] t, double orientation, bool degenerate, int smallestLevel, ref double low,
                                  ref double high)
        {
            const double epsilon = 1e-6;
            double size = 1 << level;
            double x0 = i * size, x1 = Math.Min((i + 1) * size, cellsX);
            double y0 = j * size, y1 = Math.Min((j + 1) * size, cellsY);
            double minX = Math.Min(t[0], Math.Min(t[2], t[4])), maxX = Math.Max(t[0], Math.Max(t[2], t[4]));
            double minY = Math.Min(t[1], Math.Min(t[3], t[5])), maxY = Math.Max(t[1], Math.Max(t[3], t[5]));
            if (x1 < minX - epsilon || x0 > maxX + epsilon || y1 < minY - epsilon || y0 > maxY + epsilon)
            {
                return false;
            }
            bool contained = true;
            if (!degenerate)
            {
                for (int edge = 0; edge < 3; ++edge)
                {
                    double px = t[2 * edge], py = t[2 * edge + 1];
                    double qx = t[2 * ((edge + 1) % 3)], qy = t[2 * ((edge + 1) % 3) + 1];
                    double ex = qx - px, ey = qy - py;
                    double length = Math.Sqrt(ex * ex + ey * ey);
                    if (length == 0)
                    {
                        continue;
                    }
                    // The signed distance to the edge line, positive inside; the rectangle is outside when even its best corner is
                    double best = double.MinValue, worst = double.MaxValue;
                    for (int corner = 0; corner < 4; ++corner)
                    {
                        double cornerX = (corner & 1) != 0 ? x1 : x0, cornerY = (corner & 2) != 0 ? y1 : y0;
                        double distance = orientation * (ex * (cornerY - py) - ey * (cornerX - px)) / length;
                        best = Math.Max(best, distance);
                        worst = Math.Min(worst, distance);
                    }
                    if (best < -epsilon)
                    {
                        return false;
                    }
                    if (worst < -epsilon)
                    {
                        contained = false;
                    }
                }
            }
            else
            {
                contained = false;
            }
            if (contained || level <= smallestLevel)
            {
                NodeRange(level, i, j, out double nodeLow, out double nodeHigh);
                low = Math.Min(low, nodeLow);
                high = Math.Max(high, nodeHigh);
                return true;
            }
            bool any = false;
            for (int dy = 0; dy < 2; ++dy)
            {
                for (int dx = 0; dx < 2; ++dx)
                {
                    int ci = 2 * i + dx, cj = 2 * j + dy;
                    if (ci < LevelWidth(level - 1) && cj < LevelHeight(level - 1))
                    {
                        if (TriangleNode(level - 1, ci, cj, t, orientation, degenerate, smallestLevel, ref low, ref high))
                        {
                            any = true;
                        }
                    }
                }
            }
            return any;
        }
    }

    // ShellHeightField Definition: the displacement map as normalized heights in [0, 1] on a grid of points
    public sealed class ShellHeightField : ShellHeights
    {
        // ShellHeightField Public Methods

        // rectangleResolution is the number of blocks per axis of the rectangular queries (0: an eighth of the cells, at most 128); flatness < 0 turns the flat nodes off, otherwise it is the largest deviation (in normalized heights) from the
        // triangles over the corners of a node for which the node is flat
        public ShellHeightField(int width, int height, float[] heights, ShellStorage storage = ShellStorage.Float32, double flatness = -1,
                                int rectangleResolution = 0)
            : base(width - 1, height - 1)
        {
            this.width = width;
            this.height = height;
            this.storage = storage;
            levelWidth = new int[levels];
            levelHeight = new int[levels];
            for (int level = 0; level < levels; ++level)
            {
                levelWidth[level] = LevelWidth(level);
                levelHeight[level] = LevelHeight(level);
            }
            if (storage == ShellStorage.Float32)
            {
                this.heights = heights;
                BuildFloatPyramid();
            }
            else
            {
                ushort[] quantized = new ushort[width * height];
                for (int k = 0; k < quantized.Length; ++k)
                {
                    quantized[k] = (ushort)Math.Min(Math.Max(Math.Round(heights[k] * 65535.0), 0), 65535);
                }
                if (storage == ShellStorage.Fixed16)
                {
                    this.quantized = quantized;
                }
                else
                {
                    packed = new ShellPackedHeights(width, height, quantized);
                    this.quantized = quantized;
                }
                BuildCompactPyramid();
                if (storage == ShellStorage.Packed)
                {
                    // From here on the heights are read through the codec only
                    this.quantized = null;
                }
            }
            if (flatness >= 0)
            {
                BuildFlat(flatness);
            }
            BuildRectangles(rectangleResolution > 0 ? rectangleResolution : Math.Min(128, Math.Max(8, Math.Max(cellsX, cellsY) / 8)));
        }

        public override double Sample(int x, int y)
        {
            switch (storage)
            {
                case ShellStorage.Float32:
                    return heights[y * width + x];
                case ShellStorage.Fixed16:
                    return quantized[y * width + x] * InverseQuantum;
                default:
                    return packed.Decode(x, y) * InverseQuantum;
            }
        }

        public override void NodeRange(int level, int i, int j, out double low, out double high)
        {
            if (storage == ShellStorage.Float32)
            {
                low = minimum[level][j * levelWidth[level] + i];
                high = maximum[level][j * levelWidth[level] + i];
                return;
            }
            if (level == 0)
            {
                int q00 = Quantized(i, j), q10 = Quantized(i + 1, j), q01 = Quantized(i, j + 1), q11 = Quantized(i + 1, j + 1);
                low = Math.Min(Math.Min(q00, q10), Math.Min(q01, q11)) * InverseQuantum;
                high = Math.Max(Math.Max(q00, q10), Math.Max(q01, q11)) * InverseQuantum;
                return;
            }
            low = minimumQuantized[level][j * levelWidth[level] + i] * InverseQuantum;
            high = maximumQuantized[level][j * levelWidth[level] + i] * InverseQuantum;
        }

        public override bool IsFlat(int level, int i, int j)
        {
            return flat != null && level > 0 && flat[level][j * levelWidth[level] + i] != 0;
        }

        public override bool RectangleRange(double x0, double y0, double x1, double y1, out double low, out double high)
        {
            low = 0;
            high = 0;
            const double epsilon = 1e-9;
            if (x1 < -epsilon || y1 < -epsilon || x0 > cellsX + epsilon || y0 > cellsY + epsilon)
            {
                return false;
            }
            int i0 = Math.Max((int)Math.Floor(x0 - epsilon), 0), i1 = Math.Min((int)Math.Floor(x1 + epsilon), cellsX - 1);
            int j0 = Math.Max((int)Math.Floor(y0 - epsilon), 0), j1 = Math.Min((int)Math.Floor(y1 + epsilon), cellsY - 1);
            if (i1 < i0 || j1 < j0)
            {
                return false;
            }
            if (i1 - i0 < 3 && j1 - j0 < 3)
            {
                // A few cells: exact
                double lowest = double.MaxValue, highest = double.MinValue;
                for (int j = j0; j <= j1; ++j)
                {
                    for (int i = i0; i <= i1; ++i)
                    {
                        NodeRange(0, i, j, out double cellLow, out double cellHigh);
                        lowest = Math.Min(lowest, cellLow);
                        highest = Math.Max(highest, cellHigh);
                    }
                }
                low = lowest;
                high = highest;
                return true;
            }
            // Four windows of power of two sizes over the blocks that the rectangle touches (RMIP, Eq. 10)
            int bx0 = i0 >> blockShift, bx1 = i1 >> blockShift, by0 = j0 >> blockShift, by1 = j1 >> blockShift;
            int kx = Log2(bx1 - bx0 + 1), ky = Log2(by1 - by0 + 1);
            int widthX = 1 << kx, widthY = 1 << ky;
            int stride = blocksX - widthX + 1;
            ushort[] lows = rectangleMinimum[kx * rectangleLevelsY + ky], highs = rectangleMaximum[kx * rectangleLevelsY + ky];
            int ax = bx0, bx = bx1 - widthX + 1, ay = by0, by = by1 - widthY + 1;
            low = Math.Min(Math.Min(lows[ay * stride + ax], lows[ay * stride + bx]), Math.Min(lows[by * stride + ax], lows[by * stride + bx])) *
                  InverseQuantum;
            high = Math.Max(Math.Max(highs[ay * stride + ax], highs[ay * stride + bx]),
                            Math.Max(highs[by * stride + ax], highs[by * stride + bx])) * InverseQuantum;
            return true;
        }

        // The memory of the heights, the pyramid, the flags and the rectangle tables, in bytes
        public long ByteCount()
        {
            long bytes = 0;
            if (heights != null)
            {
                bytes += 4L * heights.Length;
            }
            if (quantized != null)
            {
                bytes += 2L * quantized.Length;
            }
            if (packed != null)
            {
                bytes += packed.ByteCount();
            }
            if (minimum != null)
            {
                for (int level = 0; level < levels; ++level)
                {
                    bytes += 8L * minimum[level].Length;
                }
            }
            if (minimumQuantized != null)
            {
                for (int level = 1; level < levels; ++level)
                {
                    bytes += 4L * minimumQuantized[level].Length;
                }
            }
            if (flat != null)
            {
                for (int level = 1; level < levels; ++level)
                {
                    bytes += flat[level].Length;
                }
            }
            for (int k = 0; k < rectangleMinimum.Length; ++k)
            {
                bytes += 4L * rectangleMinimum[k].Length;
            }
            return bytes;
        }

        // The number of nodes that are flat, over all levels
        public int FlatCount()
        {
            int count = 0;
            if (flat != null)
            {
                for (int level = 1; level < levels; ++level)
                {
                    for (int k = 0; k < flat[level].Length; ++k)
                    {
                        count += flat[level][k];
                    }
                }
            }
            return count;
        }

        // ShellHeightField Public Members
        public readonly int width, height;
        public readonly ShellStorage storage;

        // ShellHeightField Private Methods
        private const double InverseQuantum = 1.0 / 65535.0;

        private int Quantized(int x, int y)
        {
            return storage == ShellStorage.Fixed16 ? quantized[y * width + x] : packed.Decode(x, y);
        }

        private static int Log2(int value)
        {
            int result = 0;
            while ((2 << result) <= value)
            {
                ++result;
            }
            return result;
        }

        private void BuildFloatPyramid()
        {
            minimum = new float[levels][];
            maximum = new float[levels][];
            minimum[0] = new float[cellsX * cellsY];
            maximum[0] = new float[cellsX * cellsY];
            for (int j = 0; j < cellsY; ++j)
            {
                for (int i = 0; i < cellsX; ++i)
                {
                    float h00 = heights[j * width + i], h10 = heights[j * width + i + 1];
                    float h01 = heights[(j + 1) * width + i], h11 = heights[(j + 1) * width + i + 1];
                    minimum[0][j * cellsX + i] = Math.Min(Math.Min(h00, h10), Math.Min(h01, h11));
                    maximum[0][j * cellsX + i] = Math.Max(Math.Max(h00, h10), Math.Max(h01, h11));
                }
            }
            for (int level = 1; level < levels; ++level)
            {
                int w = levelWidth[level], h = levelHeight[level];
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

        // The pyramid of the compact storages: no level for the cells, and 16 bit numbers (exact, they are minima and maxima of
        // quantized heights) for the others
        private void BuildCompactPyramid()
        {
            minimumQuantized = new ushort[levels][];
            maximumQuantized = new ushort[levels][];
            for (int level = 1; level < levels; ++level)
            {
                int w = levelWidth[level], h = levelHeight[level];
                minimumQuantized[level] = new ushort[w * h];
                maximumQuantized[level] = new ushort[w * h];
                for (int j = 0; j < h; ++j)
                {
                    for (int i = 0; i < w; ++i)
                    {
                        int low = 65535, high = 0;
                        if (level == 1)
                        {
                            for (int y = 2 * j; y <= Math.Min(2 * j + 2, cellsY); ++y)
                            {
                                for (int x = 2 * i; x <= Math.Min(2 * i + 2, cellsX); ++x)
                                {
                                    int q = quantized[y * width + x];
                                    low = Math.Min(low, q);
                                    high = Math.Max(high, q);
                                }
                            }
                        }
                        else
                        {
                            for (int dy = 0; dy < 2; ++dy)
                            {
                                for (int dx = 0; dx < 2; ++dx)
                                {
                                    int ci = 2 * i + dx, cj = 2 * j + dy;
                                    if (ci < levelWidth[level - 1] && cj < levelHeight[level - 1])
                                    {
                                        low = Math.Min(low, (int)minimumQuantized[level - 1][cj * levelWidth[level - 1] + ci]);
                                        high = Math.Max(high, (int)maximumQuantized[level - 1][cj * levelWidth[level - 1] + ci]);
                                    }
                                }
                            }
                        }
                        minimumQuantized[level][j * w + i] = (ushort)low;
                        maximumQuantized[level][j * w + i] = (ushort)high;
                    }
                }
            }
        }

        // Flags the nodes whose heights deviate from the two triangles over the corners of the node by at most the tolerance; only nodes
        // that lie within the grid are candidates
        private void BuildFlat(double tolerance)
        {
            flat = new byte[levels][];
            for (int level = 1; level < levels; ++level)
            {
                int size = 1 << level;
                int w = levelWidth[level], h = levelHeight[level];
                flat[level] = new byte[w * h];
                byte[] flags = flat[level];
                for (int j = 0; j < h; ++j)
                {
                    int y0 = j * size;
                    if (y0 + size > cellsY)
                    {
                        continue;
                    }
                    for (int i = 0; i < w; ++i)
                    {
                        int x0 = i * size;
                        if (x0 + size > cellsX)
                        {
                            continue;
                        }
                        double c00 = Sample(x0, y0), c10 = Sample(x0 + size, y0), c01 = Sample(x0, y0 + size);
                        double c11 = Sample(x0 + size, y0 + size);
                        bool isFlat = true;
                        for (int y = 0; y <= size && isFlat; ++y)
                        {
                            for (int x = 0; x <= size; ++x)
                            {
                                double fx = (double)x / size, fy = (double)y / size;
                                double coarse = fx >= fy ? c00 + fx * (c10 - c00) + fy * (c11 - c10)
                                                         : c00 + fx * (c11 - c01) + fy * (c01 - c00);
                                if (Math.Abs(Sample(x0 + x, y0 + y) - coarse) > tolerance)
                                {
                                    isFlat = false;
                                    break;
                                }
                            }
                        }
                        flags[j * w + i] = isFlat ? (byte)1 : (byte)0;
                    }
                }
            }
        }

        // The tables of the rectangular queries: for every window of 2^kx x 2^ky blocks, the minimum and the maximum of its blocks
        private void BuildRectangles(int resolution)
        {
            blockShift = 0;
            while (((cellsX + (1 << blockShift) - 1) >> blockShift) > resolution || ((cellsY + (1 << blockShift) - 1) >> blockShift) > resolution)
            {
                ++blockShift;
            }
            blocksX = (cellsX + (1 << blockShift) - 1) >> blockShift;
            blocksY = (cellsY + (1 << blockShift) - 1) >> blockShift;
            int levelsX = Log2(blocksX) + 1, levelsY = Log2(blocksY) + 1;
            rectangleLevelsY = levelsY;
            rectangleMinimum = new ushort[levelsX * levelsY][];
            rectangleMaximum = new ushort[levelsX * levelsY][];
            ushort[] baseMinimum = new ushort[blocksX * blocksY];
            ushort[] baseMaximum = new ushort[blocksX * blocksY];
            for (int by = 0; by < blocksY; ++by)
            {
                for (int bx = 0; bx < blocksX; ++bx)
                {
                    NodeRange(blockShift, bx, by, out double low, out double high);
                    // Rounded outwards to 16 bits: the tables bound the heights, they need not be exact
                    baseMinimum[by * blocksX + bx] = (ushort)Math.Min(Math.Max(Math.Floor(low * 65535.0 + 1e-7), 0), 65535);
                    baseMaximum[by * blocksX + bx] = (ushort)Math.Min(Math.Max(Math.Ceiling(high * 65535.0 - 1e-7), 0), 65535);
                }
            }
            rectangleMinimum[0] = baseMinimum;
            rectangleMaximum[0] = baseMaximum;
            // Doubling along x (for ky = 0), then along y from every x size
            for (int kx = 1; kx < levelsX; ++kx)
            {
                rectangleMinimum[kx * levelsY] = DoubleX(rectangleMinimum[(kx - 1) * levelsY], blocksX - (1 << (kx - 1)) + 1, blocksX - (1 << kx) + 1,
                                                         blocksY, 1 << (kx - 1), true);
                rectangleMaximum[kx * levelsY] = DoubleX(rectangleMaximum[(kx - 1) * levelsY], blocksX - (1 << (kx - 1)) + 1, blocksX - (1 << kx) + 1,
                                                         blocksY, 1 << (kx - 1), false);
            }
            for (int kx = 0; kx < levelsX; ++kx)
            {
                int stride = blocksX - (1 << kx) + 1;
                for (int ky = 1; ky < levelsY; ++ky)
                {
                    int rows = blocksY - (1 << ky) + 1;
                    rectangleMinimum[kx * levelsY + ky] = DoubleY(rectangleMinimum[kx * levelsY + ky - 1], stride, rows, 1 << (ky - 1), true);
                    rectangleMaximum[kx * levelsY + ky] = DoubleY(rectangleMaximum[kx * levelsY + ky - 1], stride, rows, 1 << (ky - 1), false);
                }
            }
        }

        private static ushort[] DoubleX(ushort[] source, int sourceStride, int stride, int rows, int shift, bool takeMinimum)
        {
            ushort[] result = new ushort[stride * rows];
            for (int y = 0; y < rows; ++y)
            {
                for (int x = 0; x < stride; ++x)
                {
                    ushort a = source[y * sourceStride + x], b = source[y * sourceStride + x + shift];
                    result[y * stride + x] = takeMinimum ? Math.Min(a, b) : Math.Max(a, b);
                }
            }
            return result;
        }

        private static ushort[] DoubleY(ushort[] source, int stride, int rows, int shift, bool takeMinimum)
        {
            ushort[] result = new ushort[stride * rows];
            for (int y = 0; y < rows; ++y)
            {
                for (int x = 0; x < stride; ++x)
                {
                    ushort a = source[y * stride + x], b = source[(y + shift) * stride + x];
                    result[y * stride + x] = takeMinimum ? Math.Min(a, b) : Math.Max(a, b);
                }
            }
            return result;
        }

        // ShellHeightField Private Members
        private readonly float[] heights;
        private ushort[] quantized;
        private readonly ShellPackedHeights packed;
        private readonly int[] levelWidth, levelHeight;
        private float[][] minimum, maximum;
        private ushort[][] minimumQuantized, maximumQuantized;
        private byte[][] flat;
        private int blockShift, blocksX, blocksY, rectangleLevelsY;
        private ushort[][] rectangleMinimum, rectangleMaximum;
    }

    // ShellPackedHeights Definition: 16 bit heights in blocks of 16 x 16 points. Each block has a plane through its corner values
    // (a fixed point predictor) and stores the residuals from the plane with as few bits as the range of the residuals needs, so the
    // codec is lossless and a height is decoded with a few operations.
    internal sealed class ShellPackedHeights
    {
        public ShellPackedHeights(int width, int height, ushort[] values)
        {
            blocksX = (width + BlockSize - 1) / BlockSize;
            int blocksY = (height + BlockSize - 1) / BlockSize;
            int blockCount = blocksX * blocksY;
            baseValue = new ushort[blockCount];
            slopeX = new int[blockCount];
            slopeY = new int[blockCount];
            minimumResidual = new int[blockCount];
            bitCount = new byte[blockCount];
            wordStart = new int[blockCount + 1];
            List<uint> words = new List<uint>();
            int[] residuals = new int[BlockSize * BlockSize];
            for (int by = 0; by < blocksY; ++by)
            {
                for (int bx = 0; bx < blocksX; ++bx)
                {
                    int block = by * blocksX + bx;
                    int x0 = bx * BlockSize, y0 = by * BlockSize;
                    int q00 = At(values, width, height, x0, y0);
                    int q10 = At(values, width, height, x0 + BlockSize - 1, y0);
                    int q01 = At(values, width, height, x0, y0 + BlockSize - 1);
                    int sx = (int)Math.Round((q10 - q00) * 256.0 / (BlockSize - 1));
                    int sy = (int)Math.Round((q01 - q00) * 256.0 / (BlockSize - 1));
                    baseValue[block] = (ushort)q00;
                    slopeX[block] = sx;
                    slopeY[block] = sy;
                    int lowest = int.MaxValue, highest = int.MinValue;
                    for (int dy = 0; dy < BlockSize; ++dy)
                    {
                        for (int dx = 0; dx < BlockSize; ++dx)
                        {
                            int residual = At(values, width, height, x0 + dx, y0 + dy) - Predict(q00, sx, sy, dx, dy);
                            residuals[dy * BlockSize + dx] = residual;
                            lowest = Math.Min(lowest, residual);
                            highest = Math.Max(highest, residual);
                        }
                    }
                    int bits = 0;
                    while ((1L << bits) <= highest - lowest)
                    {
                        ++bits;
                    }
                    minimumResidual[block] = lowest;
                    bitCount[block] = (byte)bits;
                    wordStart[block] = words.Count;
                    int wordCount = (BlockSize * BlockSize * bits + 31) / 32;
                    uint[] blockWords = new uint[wordCount + 1];
                    for (int k = 0; k < BlockSize * BlockSize && bits > 0; ++k)
                    {
                        long bitPosition = (long)k * bits;
                        int word = (int)(bitPosition >> 5), shift = (int)(bitPosition & 31);
                        ulong value = (ulong)(residuals[k] - lowest) << shift;
                        blockWords[word] |= (uint)value;
                        if (shift + bits > 32)
                        {
                            blockWords[word + 1] |= (uint)(value >> 32);
                        }
                    }
                    for (int w = 0; w < wordCount; ++w)
                    {
                        words.Add(blockWords[w]);
                    }
                }
            }
            wordStart[blockCount] = words.Count;
            words.Add(0);
            stream = words.ToArray();
        }

        public int Decode(int x, int y)
        {
            int block = (y >> BlockShift) * blocksX + (x >> BlockShift);
            int dx = x & (BlockSize - 1), dy = y & (BlockSize - 1);
            int bits = bitCount[block];
            int residual = minimumResidual[block];
            if (bits > 0)
            {
                long bitPosition = (long)(dy * BlockSize + dx) * bits;
                int word = wordStart[block] + (int)(bitPosition >> 5), shift = (int)(bitPosition & 31);
                ulong value = stream[word] | ((ulong)stream[word + 1] << 32);
                residual += (int)((value >> shift) & ((1UL << bits) - 1));
            }
            return Predict(baseValue[block], slopeX[block], slopeY[block], dx, dy) + residual;
        }

        public long ByteCount()
        {
            return 4L * stream.Length + 2L * baseValue.Length + 4L * slopeX.Length + 4L * slopeY.Length + 4L * minimumResidual.Length +
                   bitCount.Length + 4L * wordStart.Length;
        }

        private static int Predict(int baseHeight, int slopeX, int slopeY, int dx, int dy)
        {
            return baseHeight + ((slopeX * dx + slopeY * dy + 128) >> 8);
        }

        private static int At(ushort[] values, int width, int height, int x, int y)
        {
            return values[Math.Min(y, height - 1) * width + Math.Min(x, width - 1)];
        }

        private const int BlockShift = 4;
        private const int BlockSize = 16;
        private readonly int blocksX;
        private readonly ushort[] baseValue;
        private readonly int[] slopeX, slopeY, minimumResidual, wordStart;
        private readonly byte[] bitCount;
        private readonly uint[] stream;
    }

    // ShellProceduralCache Definition: the heights and the ranges of a procedural field that one thread evaluated last (direct mapped)
    internal sealed class ShellProceduralCache
    {
        public const int Size = 1 << 16;

        public ShellProceduralCache()
        {
            for (int k = 0; k < Size; ++k)
            {
                sampleKeys[k] = -1;
                rangeKeys[k] = -1;
            }
        }

        public readonly long[] sampleKeys = new long[Size];
        public readonly double[] sampleValues = new double[Size];
        public readonly long[] rangeKeys = new long[Size];
        public readonly double[] rangeLows = new double[Size];
        public readonly double[] rangeHighs = new double[Size];
    }

    // ShellProceduralField Definition: a displacement given by an expression of (u, v) over the unit square, normalized to [0, 1] by
    // the range of the interval evaluation. The grid has 2^depth cells per axis but no heights are stored.
    public sealed class ShellProceduralField : ShellHeights
    {
        // ShellProceduralField Public Methods
        public ShellProceduralField(ShellExpression expression, int depth, double scale, double offset)
            : base(1 << depth, 1 << depth)
        {
            this.expression = expression;
            this.scale = scale;
            this.offset = offset;
            // The range of the function: the union of the interval evaluations over blocks of the square
            int blocks = Math.Min(32, 1 << depth);
            double lowest = double.MaxValue, highest = double.MinValue;
            for (int by = 0; by < blocks; ++by)
            {
                for (int bx = 0; bx < blocks; ++bx)
                {
                    expression.Bounds((double)bx / blocks, (double)(bx + 1) / blocks, (double)by / blocks, (double)(by + 1) / blocks,
                                      out double low, out double high);
                    double a = scale * low + offset, b = scale * high + offset;
                    lowest = Math.Min(lowest, Math.Min(a, b));
                    highest = Math.Max(highest, Math.Max(a, b));
                }
            }
            if (!(highest - lowest > 1e-9) || double.IsInfinity(highest - lowest))
            {
                if (double.IsInfinity(lowest) || double.IsInfinity(highest) || double.IsNaN(highest - lowest))
                {
                    throw new FormatException("the displacement function has an unbounded range");
                }
                highest = lowest + 1e-6;
            }
            this.low = lowest;
            this.high = highest;
        }

        public override double Sample(int x, int y)
        {
            // Rays that are near each other visit the same cells, so the values are kept in a small cache per thread
            ShellProceduralCache cache = caches.Value;
            long key = ((long)x << 32) | (uint)y;
            int index = CacheIndex(key);
            if (cache.sampleKeys[index] == key)
            {
                return cache.sampleValues[index];
            }
            double value = scale * expression.Evaluate((double)x / cellsX, (double)y / cellsY) + offset;
            value = (value - low) / (high - low);
            cache.sampleKeys[index] = key;
            cache.sampleValues[index] = value;
            return value;
        }

        public override void NodeRange(int level, int i, int j, out double rangeLow, out double rangeHigh)
        {
            if (level == 0)
            {
                // The surface over a cell is made of the four corner values, so they give the range exactly
                double h00 = Sample(i, j), h10 = Sample(i + 1, j), h01 = Sample(i, j + 1), h11 = Sample(i + 1, j + 1);
                rangeLow = Math.Min(Math.Min(h00, h10), Math.Min(h01, h11));
                rangeHigh = Math.Max(Math.Max(h00, h10), Math.Max(h01, h11));
                return;
            }
            ShellProceduralCache cache = caches.Value;
            long key = ((long)level << 50) | ((long)i << 25) | (long)j;
            int index = CacheIndex(key);
            if (cache.rangeKeys[index] == key)
            {
                rangeLow = cache.rangeLows[index];
                rangeHigh = cache.rangeHighs[index];
                return;
            }
            double size = 1 << level;
            Bounds(i * size, Math.Min((i + 1) * size, cellsX), j * size, Math.Min((j + 1) * size, cellsY), out rangeLow, out rangeHigh);
            cache.rangeKeys[index] = key;
            cache.rangeLows[index] = rangeLow;
            cache.rangeHighs[index] = rangeHigh;
        }

        public override bool RectangleRange(double x0, double y0, double x1, double y1, out double rangeLow, out double rangeHigh)
        {
            rangeLow = 0;
            rangeHigh = 0;
            const double epsilon = 1e-9;
            if (x1 < -epsilon || y1 < -epsilon || x0 > cellsX + epsilon || y0 > cellsY + epsilon)
            {
                return false;
            }
            int i0 = Math.Max((int)Math.Floor(x0 - epsilon), 0), i1 = Math.Min((int)Math.Floor(x1 + epsilon), cellsX - 1);
            int j0 = Math.Max((int)Math.Floor(y0 - epsilon), 0), j1 = Math.Min((int)Math.Floor(y1 + epsilon), cellsY - 1);
            if (i1 < i0 || j1 < j0)
            {
                return false;
            }
            Bounds(i0, i1 + 1, j0, j1 + 1, out rangeLow, out rangeHigh);
            return true;
        }

        // The displacement range of the function in the units of scale and offset: normalized heights 0 and 1
        public double Low()
        {
            return low;
        }

        public double High()
        {
            return high;
        }

        // ShellProceduralField Private Methods
        private static int CacheIndex(long key)
        {
            return (int)(((ulong)key * 0x9E3779B97F4A7C15UL) >> 48) & (ShellProceduralCache.Size - 1);
        }

        private void Bounds(double x0, double x1, double y0, double y1, out double rangeLow, out double rangeHigh)
        {
            expression.Bounds(x0 / cellsX, x1 / cellsX, y0 / cellsY, y1 / cellsY, out double a, out double b);
            double p = (scale * a + offset - low) / (high - low), q = (scale * b + offset - low) / (high - low);
            rangeLow = Math.Min(p, q);
            rangeHigh = Math.Max(p, q);
        }

        // ShellProceduralField Private Members
        private readonly System.Threading.ThreadLocal<ShellProceduralCache> caches = new System.Threading.ThreadLocal<ShellProceduralCache>(() => new ShellProceduralCache());
        private readonly ShellExpression expression;
        private readonly double scale, offset, low, high;
    }

}  // namespace pbrt
