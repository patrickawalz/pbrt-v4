// (not in pbrt-v4) temporalgrid.cs
//
// Volumetric motion blur by temporally unstructured volumes: Wrenninge, "Efficient Rendering of Volumetric Motion Blur Using
// Temporally Unstructured Volumes", Journal of Computer Graphics Techniques 5(1), 2016.
//
// The density of a medium that moves is a function of the place and the time, and a renderer only ever asks for its value: the
// motion itself is not asked for. A temporally unstructured volume (TemporalGrid) is a grid in space whose voxels each hold a
// function of time, as a list of (time, value) pairs of any number at any times: two pairs are enough for a density that
// changes linearly, however fast, and a voxel through which nothing passes holds none. A lookup finds the eight voxels about
// the point, interpolates each in time and then the eight in space (the paper's quadrilinear interpolation, section 3.3). The
// voxels are kept in blocks of 8^3 with one table of offsets and the times and the values of the block in two arrays (section
// 3.1, figure 4); a block without a pair is not allocated. The functions are compressed (section 3.2): repeated values are
// taken out, then the vertices whose removal changes the function the least, while the change is below a tolerance relative
// to the ends of the interval; a vertex of value zero stays (taking it out would put material at a time where there is none).
//
// The volumes are made here in two ways (TemporalGridBuilder):
//   * from the densities at several times ("integer nt" grids, "float times"): the states of a simulation within the frame
//     (section 5), each voxel's function being its values at those times;
//   * from a density and a field of velocities, by sampling the place and the time (the rasterization step of the paper's
//     Reves, section 4.7): the density of a voxel at a time of the shutter is the density at the place the material came
//     from, followed back along the field in "steps" steps (one step: the straight line of Eulerian motion blur; more: the
//     curved path).
// The whole of Reves (the bounding, splitting and dicing of primitives and their shaders, sections 4.2 to 4.6) is not here.
//
// For comparison, "bool eulerian" renders the same density and velocities the way the paper's appendix A.1 describes: nothing
// is made beforehand, every lookup follows the field back, and the majorants are those of the density within the reach of the
// motion.
//
// MakeNamedMedium "smoke" "string type" "temporalgrid"
//   "integer nx" "integer ny" "integer nz"   the grid
//   "float density"                          nx ny nz values, or nx ny nz nt of them with
//   "integer nt"  "float times"              the number of states and their times (the shutter, evenly, without "times")
//   "float velocity"                         3 values (one velocity), or 3 for each voxel of nvx nvy nvz ("integer nvx" ...):
//                                            the units of the medium's space (that of p0 and p1) a unit of time
//   "float time0"                            the time of the density that has velocities (0)
//   "float shutter"                          the times the functions are made for: [ open close ] ([ 0 1 ])
//   "integer timesamples"                    the samples of the time for a voxel, of a density with velocities (8)
//   "integer positionsamples"                the places within the voxel for each (1: its middle)
//   "integer steps"                          the steps of the way back along the velocities (1)
//   "float tolerance"                        of the compression, relative (0.01)
//   "bool eulerian"                          no functions of time: the velocities are followed at every lookup (false)
//   "point3 p0" "point3 p1" "spectrum sigma_a" "spectrum sigma_s" "float scale" "float g"    as "uniformgrid"

using System;
using System.Collections.Generic;

namespace pbrt
{
    // TemporalGrid Definition
    public sealed unsafe class TemporalGrid
    {
        // TemporalGrid Public Methods
        public const int BlockSize = 8;
        public const int BlockVoxels = BlockSize * BlockSize * BlockSize;

        public TemporalGrid(int nx, int ny, int nz, int[][] offsets, Float[][] times, Float[][] values)
        {
            this.nx = nx;
            this.ny = ny;
            this.nz = nz;
            blocksX = (nx + BlockSize - 1) / BlockSize;
            blocksY = (ny + BlockSize - 1) / BlockSize;
            this.offsets = offsets;
            this.times = times;
            this.values = values;
        }

        public int XSize
        {
            get { return nx; }
        }

        public int YSize
        {
            get { return ny; }
        }

        public int ZSize
        {
            get { return nz; }
        }

        // The number of (time, value) pairs of the whole grid, and of blocks that hold any
        public long SampleCount()
        {
            long count = 0;
            for (int block = 0; block < offsets.Length; ++block)
            {
                count += offsets[block] != null ? times[block].Length : 0;
            }
            return count;
        }

        public int BlockCount()
        {
            int count = 0;
            for (int block = 0; block < offsets.Length; ++block)
            {
                count += offsets[block] != null ? 1 : 0;
            }
            return count;
        }

        public long BytesAllocated()
        {
            return 2 * sizeof(Float) * SampleCount() + (long)sizeof(int) * (BlockVoxels + 1) * BlockCount() + 3L * IntPtr.Size * offsets.Length;
        }

        // The value of a voxel at a time: its function of time, linear between its pairs and constant before the first and
        // after the last; zero for a voxel without pairs and outside the grid
        public Float Voxel(int x, int y, int z, Float time)
        {
            if ((uint)x >= (uint)nx || (uint)y >= (uint)ny || (uint)z >= (uint)nz)
            {
                return 0;
            }
            int block = (x / BlockSize) + blocksX * ((y / BlockSize) + blocksY * (z / BlockSize));
            int[] blockOffsets = offsets[block];
            if (blockOffsets == null)
            {
                return 0;
            }
            int voxel = (x % BlockSize) + BlockSize * ((y % BlockSize) + BlockSize * (z % BlockSize));
            int first = blockOffsets[voxel], end = blockOffsets[voxel + 1];
            if (first == end)
            {
                return 0;
            }
            Float[] blockTimes = times[block];
            Float[] blockValues = values[block];
            if (end - first == 1 || time <= blockTimes[first])
            {
                return blockValues[first];
            }
            if (time >= blockTimes[end - 1])
            {
                return blockValues[end - 1];
            }
            // The interval of the time: the last pair at or before it
            int low = first, high = end - 1;
            while (high - low > 1)
            {
                int middle = (low + high) / 2;
                if (blockTimes[middle] <= time)
                {
                    low = middle;
                }
                else
                {
                    high = middle;
                }
            }
            Float t = (time - blockTimes[low]) / (blockTimes[high] - blockTimes[low]);
            return blockValues[low] + t * (blockValues[high] - blockValues[low]);
        }

        // The largest value of a voxel over all times
        public Float VoxelMax(int x, int y, int z)
        {
            int block = (x / BlockSize) + blocksX * ((y / BlockSize) + blocksY * (z / BlockSize));
            int[] blockOffsets = offsets[block];
            if (blockOffsets == null)
            {
                return 0;
            }
            int voxel = (x % BlockSize) + BlockSize * ((y % BlockSize) + BlockSize * (z % BlockSize));
            Float[] blockValues = values[block];
            Float largest = 0;
            for (int i = blockOffsets[voxel]; i < blockOffsets[voxel + 1]; ++i)
            {
                largest = Math.Max(largest, blockValues[i]);
            }
            return largest;
        }

        // The value at a point of the unit cube at a time: in time within each of the eight voxels about the point, then
        // trilinear between them (the values of the voxels are at their middles, as those of SampledGrid)
        public Float Lookup(Point3f p, Float time)
        {
            Float sx = p.x * nx - 0.5f, sy = p.y * ny - 0.5f, sz = p.z * nz - 0.5f;
            int x = (int)MathF.Floor(sx), y = (int)MathF.Floor(sy), z = (int)MathF.Floor(sz);
            Float dx = sx - x, dy = sy - y, dz = sz - z;
            Float d00 = Voxel(x, y, z, time) + dx * (Voxel(x + 1, y, z, time) - Voxel(x, y, z, time));
            Float d10 = Voxel(x, y + 1, z, time) + dx * (Voxel(x + 1, y + 1, z, time) - Voxel(x, y + 1, z, time));
            Float d01 = Voxel(x, y, z + 1, time) + dx * (Voxel(x + 1, y, z + 1, time) - Voxel(x, y, z + 1, time));
            Float d11 = Voxel(x, y + 1, z + 1, time) + dx * (Voxel(x + 1, y + 1, z + 1, time) - Voxel(x, y + 1, z + 1, time));
            Float d0 = d00 + dy * (d10 - d00), d1 = d01 + dy * (d11 - d01);
            return d0 + dz * (d1 - d0);
        }

        // The largest value over a box of the unit cube, at any time: that of the voxels a lookup in the box reads
        public Float MaxValue(Bounds3f box)
        {
            int x0 = Math.Max(0, (int)MathF.Floor(box.pMin.x * nx - 0.5f)), x1 = Math.Min(nx - 1, (int)MathF.Floor(box.pMax.x * nx - 0.5f) + 1);
            int y0 = Math.Max(0, (int)MathF.Floor(box.pMin.y * ny - 0.5f)), y1 = Math.Min(ny - 1, (int)MathF.Floor(box.pMax.y * ny - 0.5f) + 1);
            int z0 = Math.Max(0, (int)MathF.Floor(box.pMin.z * nz - 0.5f)), z1 = Math.Min(nz - 1, (int)MathF.Floor(box.pMax.z * nz - 0.5f) + 1);
            Float largest = 0;
            for (int z = z0; z <= z1; ++z)
            {
                for (int y = y0; y <= y1; ++y)
                {
                    for (int x = x0; x <= x1; ++x)
                    {
                        largest = Math.Max(largest, VoxelMax(x, y, z));
                    }
                }
            }
            return largest;
        }

        // TemporalGrid Private Members
        private readonly int nx, ny, nz, blocksX, blocksY;
        // For each block (null: none of its voxels has a pair): the place of the pairs of each of its voxels, one more than
        // there are voxels (the pairs of a voxel are from its offset to the next), and the times and values of the block
        private readonly int[][] offsets;
        private readonly Float[][] times;
        private readonly Float[][] values;
    }

    // TemporalGridBuilder Definition
    public sealed unsafe class TemporalGridBuilder
    {
        // TemporalGridBuilder Public Methods

        // A grid whose voxels take up to `capacity` pairs each
        public TemporalGridBuilder(int nx, int ny, int nz, int capacity)
        {
            this.nx = nx;
            this.ny = ny;
            this.nz = nz;
            this.capacity = capacity;
            counts = new int[nx * ny * nz];
            times = new Float[(long)nx * ny * nz * capacity];
            values = new Float[(long)nx * ny * nz * capacity];
        }

        // A pair of a voxel, at any time and in any order (the pairs of one voxel are added by one thread)
        public void Add(int x, int y, int z, Float time, Float value)
        {
            int voxel = x + nx * (y + ny * z);
            long at = (long)voxel * capacity + counts[voxel]++;
            times[at] = time;
            values[at] = value;
        }

        // The function of time through the n pairs, compressed in place: sorted by time; without the values that repeat
        // (linear between the pairs, a run of the same value needs only its ends, and a function that is zero needs no pair);
        // then without the vertices whose removal changes the function the least, as long as the change at the vertex is
        // within the tolerance times the larger of the values at the ends of its interval. A vertex of value zero stays.
        // Returns the number of pairs that are left.
        public static int Compress(Float* times, Float* values, int n, Float tolerance)
        {
            // Sort by time (few pairs: by insertion)
            for (int i = 1; i < n; ++i)
            {
                Float time = times[i], value = values[i];
                int j = i - 1;
                for (; j >= 0 && times[j] > time; --j)
                {
                    times[j + 1] = times[j];
                    values[j + 1] = values[j];
                }
                times[j + 1] = time;
                values[j + 1] = value;
            }
            // One pair for each time (two samples of the same time: their mean)
            int kept = 0;
            for (int i = 0; i < n;)
            {
                int j = i;
                double sum = 0;
                for (; j < n && times[j] == times[i]; ++j)
                {
                    sum += values[j];
                }
                times[kept] = times[i];
                values[kept++] = (Float)(sum / (j - i));
                i = j;
            }
            n = kept;
            // Repeated values
            kept = 0;
            for (int i = 0; i < n; ++i)
            {
                if (i > 0 && i < n - 1 && values[i] == values[kept - 1] && values[i] == values[i + 1])
                {
                    continue;
                }
                times[kept] = times[i];
                values[kept++] = values[i];
            }
            n = kept;
            if (n == 2 && values[0] == values[1])
            {
                n = 1;
            }
            if (n == 1 && values[0] == 0)
            {
                return 0;
            }
            // Line simplification: the vertex of the least error first
            while (n > 2)
            {
                int best = -1;
                Float least = Infinity;
                for (int i = 1; i < n - 1; ++i)
                {
                    if (values[i] == 0)
                    {
                        continue;
                    }
                    Float t = (times[i] - times[i - 1]) / (times[i + 1] - times[i - 1]);
                    Float error = Math.Abs(values[i] - (values[i - 1] + t * (values[i + 1] - values[i - 1])));
                    if (error <= tolerance * Math.Max(Math.Abs(values[i - 1]), Math.Abs(values[i + 1])) && error < least)
                    {
                        least = error;
                        best = i;
                    }
                }
                if (best < 0)
                {
                    break;
                }
                for (int i = best; i < n - 1; ++i)
                {
                    times[i] = times[i + 1];
                    values[i] = values[i + 1];
                }
                --n;
            }
            return n;
        }

        // The grid of the pairs that were added, each voxel's function compressed
        public TemporalGrid Build(Float tolerance)
        {
            // Compress the voxels (each by itself: in parallel)
            ParallelFor(0, nz, (int z) =>
            {
                fixed (Float* timesPointer = times, valuesPointer = values)
                {
                    for (int y = 0; y < ny; ++y)
                    {
                        for (int x = 0; x < nx; ++x)
                        {
                            int voxel = x + nx * (y + ny * z);
                            counts[voxel] = Compress(timesPointer + (long)voxel * capacity, valuesPointer + (long)voxel * capacity, counts[voxel], tolerance);
                        }
                    }
                }
            });
            // The blocks
            int size = TemporalGrid.BlockSize;
            int blocksX = (nx + size - 1) / size, blocksY = (ny + size - 1) / size, blocksZ = (nz + size - 1) / size;
            int[][] blockOffsets = new int[blocksX * blocksY * blocksZ][];
            Float[][] blockTimes = new Float[blockOffsets.Length][];
            Float[][] blockValues = new Float[blockOffsets.Length][];
            ParallelFor(0, blockOffsets.Length, (int block) =>
            {
                int bx = block % blocksX, by = (block / blocksX) % blocksY, bz = block / (blocksX * blocksY);
                int[] offsets = new int[TemporalGrid.BlockVoxels + 1];
                int total = 0;
                for (int v = 0; v < TemporalGrid.BlockVoxels; ++v)
                {
                    int x = bx * size + v % size, y = by * size + (v / size) % size, z = bz * size + v / (size * size);
                    offsets[v] = total;
                    total += x < nx && y < ny && z < nz ? counts[x + nx * (y + ny * z)] : 0;
                }
                offsets[TemporalGrid.BlockVoxels] = total;
                if (total == 0)
                {
                    return;
                }
                Float[] packedTimes = new Float[total], packedValues = new Float[total];
                for (int v = 0; v < TemporalGrid.BlockVoxels; ++v)
                {
                    int x = bx * size + v % size, y = by * size + (v / size) % size, z = bz * size + v / (size * size);
                    if (x < nx && y < ny && z < nz)
                    {
                        long from = (long)(x + nx * (y + ny * z)) * capacity;
                        for (int i = 0; i < offsets[v + 1] - offsets[v]; ++i)
                        {
                            packedTimes[offsets[v] + i] = times[from + i];
                            packedValues[offsets[v] + i] = values[from + i];
                        }
                    }
                }
                blockOffsets[block] = offsets;
                blockTimes[block] = packedTimes;
                blockValues[block] = packedValues;
            });
            return new TemporalGrid(nx, ny, nz, blockOffsets, blockTimes, blockValues);
        }

        // The grid of a density at several times: density[t][z][y][x], nt states at the times given
        public static TemporalGrid FromStates(Float[] density, int nx, int ny, int nz, Float[] stateTimes, Float tolerance)
        {
            int nt = stateTimes.Length;
            TemporalGridBuilder builder = new TemporalGridBuilder(nx, ny, nz, nt);
            ParallelFor(0, nz, (int z) =>
            {
                for (int y = 0; y < ny; ++y)
                {
                    for (int x = 0; x < nx; ++x)
                    {
                        for (int t = 0; t < nt; ++t)
                        {
                            builder.Add(x, y, z, stateTimes[t], density[x + nx * (y + ny * (z + (long)nz * t))]);
                        }
                    }
                }
            });
            return builder.Build(tolerance);
        }

        // The grid of a density that moves with a field of velocities: for each voxel, `timeSamples` times of the shutter (one
        // in each of as many equal parts of it, and its two ends), and at each the density at `positionSamples` places of the
        // voxel (one: its middle), followed back to the time of the density (TemporalGridMedium.Advected)
        public static TemporalGrid FromVelocities(SampledGrid<Float> density, int nx, int ny, int nz, SampledGrid<Float> velocityX, SampledGrid<Float> velocityY,
                                                  SampledGrid<Float> velocityZ, Float time0, Float shutterOpen, Float shutterClose, int timeSamples,
                                                  int positionSamples, int steps, Float tolerance)
        {
            TemporalGridBuilder builder = new TemporalGridBuilder(nx, ny, nz, timeSamples + 2);
            ParallelFor(0, nz, (int z) =>
            {
                RNG rng = new RNG((ulong)z);
                for (int y = 0; y < ny; ++y)
                {
                    for (int x = 0; x < nx; ++x)
                    {
                        for (int s = -1; s <= timeSamples; ++s)
                        {
                            Float time = s < 0 ? shutterOpen : s == timeSamples ? shutterClose
                                                             : shutterOpen + (shutterClose - shutterOpen) * (s + rng.Uniform<Float>()) / timeSamples;
                            double sum = 0;
                            for (int j = 0; j < positionSamples; ++j)
                            {
                                Float jx = positionSamples == 1 ? 0.5f : rng.Uniform<Float>();
                                Float jy = positionSamples == 1 ? 0.5f : rng.Uniform<Float>();
                                Float jz = positionSamples == 1 ? 0.5f : (j + rng.Uniform<Float>()) / positionSamples;
                                Point3f p = new Point3f((x + jx) / nx, (y + jy) / ny, (z + jz) / nz);
                                sum += TemporalGridMedium.Advected(density, velocityX, velocityY, velocityZ, p, time - time0, steps);
                            }
                            builder.Add(x, y, z, time, (Float)(sum / positionSamples));
                        }
                    }
                }
            });
            return builder.Build(tolerance);
        }

        // TemporalGridBuilder Private Members
        private readonly int nx, ny, nz, capacity;
        private readonly int[] counts;
        private readonly Float[] times, values;
    }

    // TemporalGridMedium Definition
    public sealed unsafe class TemporalGridMedium : Medium, IMajorantMedium<DDAMajorantIterator>
    {
        // TemporalGridMedium Public Methods

        // grid: the functions of time; or null, and the density with its velocities (those of the unit cube of the medium a
        // unit of time), followed at every lookup ("eulerian")
        public TemporalGridMedium(Bounds3f bounds, Transform renderFromMedium, Spectrum sigma_a, Spectrum sigma_s, Float sigmaScale, Float g, TemporalGrid grid,
                                  SampledGrid<Float> density, SampledGrid<Float> velocityX, SampledGrid<Float> velocityY, SampledGrid<Float> velocityZ,
                                  Float maxSpeed, Float time0, Float shutterOpen, Float shutterClose, int steps, Allocator alloc)
        {
            this.bounds = bounds;
            this.renderFromMedium = renderFromMedium;
            sigma_a_spec = new DenselySampledSpectrum(sigma_a, alloc);
            sigma_s_spec = new DenselySampledSpectrum(sigma_s, alloc);
            sigma_a_spec.Scale(sigmaScale);
            sigma_s_spec.Scale(sigmaScale);
            phase = new HGPhaseFunction(g, HGPhaseFunction.creationRayleigh);
            this.grid = grid;
            this.density = density;
            this.velocityX = velocityX;
            this.velocityY = velocityY;
            this.velocityZ = velocityZ;
            this.time0 = time0;
            this.shutterOpen = shutterOpen;
            this.shutterClose = shutterClose;
            this.steps = steps;
            // The majorants: of the functions over all their times; or of the density within the reach of the motion over the
            // shutter (the padding that the functions of time do without)
            majorantGrid = new MajorantGrid(bounds, new Point3i(16, 16, 16), alloc);
            Float reach = maxSpeed * Math.Max(Math.Abs(shutterOpen - time0), Math.Abs(shutterClose - time0));
            for (int z = 0; z < majorantGrid.res.z; ++z)
            {
                for (int y = 0; y < majorantGrid.res.y; ++y)
                {
                    for (int x = 0; x < majorantGrid.res.x; ++x)
                    {
                        Bounds3f box = majorantGrid.VoxelBounds(x, y, z);
                        if (grid != null)
                        {
                            majorantGrid.Set(x, y, z, grid.MaxValue(box));
                        }
                        else
                        {
                            Vector3f pad = new Vector3f(reach, reach, reach);
                            majorantGrid.Set(x, y, z, density.MaxValue(new Bounds3f(box.pMin - pad, box.pMax + pad)));
                        }
                    }
                }
            }
        }

        // The density at a point of the unit cube that the material at that point had `elapsed` before: followed back along
        // the velocities in `steps` steps (one: a straight line with the velocity at the point, the Eulerian motion blur of the
        // paper's appendix A.1; more: the path through the field, which bends)
        public static Float Advected(SampledGrid<Float> density, SampledGrid<Float> velocityX, SampledGrid<Float> velocityY, SampledGrid<Float> velocityZ,
                                     Point3f p, Float elapsed, int steps)
        {
            Float step = elapsed / steps;
            for (int i = 0; i < steps; ++i)
            {
                p = new Point3f(p.x - step * velocityX.Lookup(p), p.y - step * velocityY.Lookup(p), p.z - step * velocityZ.Lookup(p));
            }
            return density.Lookup(p);
        }

        // The density at a point of the unit cube at a time
        public Float Density(Point3f p, Float time)
        {
            ++lookups;
            if (grid != null)
            {
                return grid.Lookup(p, time);
            }
            return Advected(density, velocityX, velocityY, velocityZ, p, time - time0, steps);
        }

        public TemporalGrid Grid
        {
            get { return grid; }
        }

        public override bool IsEmissive()
        {
            return false;
        }

        // (without a time: the start of the shutter)
        public override MediumProperties SamplePoint(Point3f p, SampledWavelengths lambda)
        {
            return SamplePoint(p, lambda, shutterOpen);
        }

        public override MediumProperties SamplePoint(Point3f p, SampledWavelengths lambda, Float time)
        {
            p = renderFromMedium.ApplyInverse(p);
            p = (Point3f)bounds.Offset(p);
            Float d = Density(p, time);
            return new MediumProperties { sigma_a = sigma_a_spec.Sample(lambda) * d, sigma_s = sigma_s_spec.Sample(lambda) * d, phase = phase, Le = new SampledSpectrum(0f) };
        }

        public DDAMajorantIterator SampleRay(Ray ray, Float raytMax, SampledWavelengths lambda)
        {
            ray = renderFromMedium.ApplyInverse(ray, &raytMax);
            Float tMin, tMax;
            if (!bounds.IntersectP(ray.o, ray.d, raytMax, &tMin, &tMax))
            {
                return new DDAMajorantIterator();
            }
            return new DDAMajorantIterator(ray, tMin, tMax, majorantGrid, sigma_a_spec.Sample(lambda) + sigma_s_spec.Sample(lambda));
        }

        public override RayMajorantIterator SampleRay(Ray ray, Float tMax, SampledWavelengths lambda, ScratchBuffer buf)
        {
            return buf.Alloc(SampleRay(ray, tMax, lambda));
        }

        public override string ToString()
        {
            return StringPrintf("[ TemporalGridMedium bounds: %s eulerian: %s ]", bounds, grid == null);
        }

        public static TemporalGridMedium Create(ParameterDictionary parameters, Transform renderFromMedium, FileLoc loc, Allocator alloc)
        {
            int nx = parameters.GetOneInt("nx", 1), ny = parameters.GetOneInt("ny", 1), nz = parameters.GetOneInt("nz", 1);
            int nt = parameters.GetOneInt("nt", 1);
            List<Float> densityList = parameters.GetFloatArray("density");
            List<Float> timeList = parameters.GetFloatArray("times");
            List<Float> velocityList = parameters.GetFloatArray("velocity");
            List<Float> shutter = parameters.GetFloatArray("shutter");
            Float time0 = parameters.GetOneFloat("time0", 0f);
            int timeSamples = parameters.GetOneInt("timesamples", 8);
            int positionSamples = parameters.GetOneInt("positionsamples", 1);
            int steps = parameters.GetOneInt("steps", 1);
            Float tolerance = parameters.GetOneFloat("tolerance", 0.01f);
            bool eulerian = parameters.GetOneBool("eulerian", false);
            if (nx < 1 || ny < 1 || nz < 1 || nt < 1 || (long)densityList.Count != (long)nx * ny * nz * nt)
                ErrorExit(loc, StringPrintf("\"temporalgrid\": \"density\" has %d values; expected nx*ny*nz*nt = %d.", densityList.Count, (long)nx * ny * nz * nt));
            if (shutter.Count != 0 && (shutter.Count != 2 || !(shutter[1] > shutter[0])))
                ErrorExit(loc, "\"temporalgrid\": \"shutter\" is the two times [ open close ], the second after the first.");
            if (timeSamples < 1 || positionSamples < 1 || steps < 1 || !(tolerance >= 0))
                ErrorExit(loc, "\"temporalgrid\": \"timesamples\", \"positionsamples\" and \"steps\" are 1 or more, and \"tolerance\" is not negative.");
            Float shutterOpen = shutter.Count == 2 ? shutter[0] : 0, shutterClose = shutter.Count == 2 ? shutter[1] : 1;
            Point3f p0 = parameters.GetOnePoint3f("p0", new Point3f(0f, 0f, 0f));
            Point3f p1 = parameters.GetOnePoint3f("p1", new Point3f(1f, 1f, 1f));
            Float g = parameters.GetOneFloat("g", 0f);
            Spectrum sigma_a = parameters.GetOneSpectrum("sigma_a", null, SpectrumType.Unbounded, alloc);
            if (sigma_a == null)
                sigma_a = new ConstantSpectrum(1f);
            Spectrum sigma_s = parameters.GetOneSpectrum("sigma_s", null, SpectrumType.Unbounded, alloc);
            if (sigma_s == null)
                sigma_s = new ConstantSpectrum(1f);
            Float sigmaScale = parameters.GetOneFloat("scale", 1f);
            Bounds3f bounds = new Bounds3f(p0, p1);

            if (nt > 1)
            {
                // The states of the density at their times
                if (velocityList.Count != 0 || eulerian)
                    ErrorExit(loc, "\"temporalgrid\": a density at several times (\"nt\") has no \"velocity\" and is not \"eulerian\".");
                if (timeList.Count != 0 && timeList.Count != nt)
                    ErrorExit(loc, StringPrintf("\"temporalgrid\": \"times\" has %d values; expected nt = %d.", timeList.Count, nt));
                Float[] stateTimes = new Float[nt];
                for (int t = 0; t < nt; ++t)
                {
                    stateTimes[t] = timeList.Count != 0 ? timeList[t] : shutterOpen + (shutterClose - shutterOpen) * t / (nt - 1);
                    if (t > 0 && !(stateTimes[t] > stateTimes[t - 1]))
                        ErrorExit(loc, "\"temporalgrid\": the \"times\" rise.");
                }
                TemporalGrid states = TemporalGridBuilder.FromStates(densityList.ToArray(), nx, ny, nz, stateTimes, tolerance);
                return new TemporalGridMedium(bounds, renderFromMedium, sigma_a, sigma_s, sigmaScale, g, states, null, null, null, null, 0, time0, shutterOpen,
                                              shutterClose, steps, alloc);
            }

            // A density with velocities
            int nvx = parameters.GetOneInt("nvx", velocityList.Count == 3 ? 1 : nx), nvy = parameters.GetOneInt("nvy", velocityList.Count == 3 ? 1 : ny);
            int nvz = parameters.GetOneInt("nvz", velocityList.Count == 3 ? 1 : nz);
            if (velocityList.Count == 0)
            {
                velocityList = new List<Float> { 0, 0, 0 };
                nvx = 1;
                nvy = 1;
                nvz = 1;
            }
            if ((long)velocityList.Count != 3L * nvx * nvy * nvz)
                ErrorExit(loc, StringPrintf("\"temporalgrid\": \"velocity\" has %d values; expected 3, or 3*nvx*nvy*nvz = %d.", velocityList.Count, 3L * nvx * nvy * nvz));
            // (the velocities in the units of the unit cube of the medium)
            Vector3f extent = bounds.Diagonal();
            Float[] vx = new Float[nvx * nvy * nvz], vy = new Float[vx.Length], vz = new Float[vx.Length];
            Float maxSpeed = 0;
            for (int i = 0; i < vx.Length; ++i)
            {
                vx[i] = velocityList[3 * i] / extent.x;
                vy[i] = velocityList[3 * i + 1] / extent.y;
                vz[i] = velocityList[3 * i + 2] / extent.z;
                maxSpeed = Math.Max(maxSpeed, Math.Max(Math.Abs(vx[i]), Math.Max(Math.Abs(vy[i]), Math.Abs(vz[i]))));
            }
            SampledGrid<Float> density = new SampledGrid<Float>(densityList.ToArray(), nx, ny, nz, alloc);
            SampledGrid<Float> velocityX = new SampledGrid<Float>(vx, nvx, nvy, nvz, alloc);
            SampledGrid<Float> velocityY = new SampledGrid<Float>(vy, nvx, nvy, nvz, alloc);
            SampledGrid<Float> velocityZ = new SampledGrid<Float>(vz, nvx, nvy, nvz, alloc);
            if (eulerian)
            {
                return new TemporalGridMedium(bounds, renderFromMedium, sigma_a, sigma_s, sigmaScale, g, null, density, velocityX, velocityY, velocityZ, maxSpeed,
                                              time0, shutterOpen, shutterClose, steps, alloc);
            }
            TemporalGrid sampled = TemporalGridBuilder.FromVelocities(density, nx, ny, nz, velocityX, velocityY, velocityZ, time0, shutterOpen, shutterClose,
                                                                      timeSamples, positionSamples, steps, tolerance);
            return new TemporalGridMedium(bounds, renderFromMedium, sigma_a, sigma_s, sigmaScale, g, sampled, null, null, null, null, 0, time0, shutterOpen,
                                          shutterClose, steps, alloc);
        }

        // The number of lookups of the density by this thread
        [ThreadStatic] public static long lookups;

        // TemporalGridMedium Private Members
        private readonly Bounds3f bounds;
        private readonly Transform renderFromMedium;
        private readonly DenselySampledSpectrum sigma_a_spec, sigma_s_spec;
        private readonly HGPhaseFunction phase;
        private readonly TemporalGrid grid;
        private readonly SampledGrid<Float> density, velocityX, velocityY, velocityZ;
        private readonly Float time0, shutterOpen, shutterClose;
        private readonly int steps;
        private readonly MajorantGrid majorantGrid;
    }
}
