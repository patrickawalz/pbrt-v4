// (not in pbrt-v4) refractivefield.cs
//
// The index of refraction of a refractive medium as a field whose light paths are known in closed form, and those paths.
//
// A light path in a medium of index n(x) obeys the ray equation d/ds (n dx/ds) = grad n, with s the arc length. With the
// parameter sigma for which ds = n dsigma, and the optical velocity v = dx/dsigma (whose length is n), it reads
//
//     d2x / dsigma2 = 1/2 grad (n^2).
//
// Where n^2 is a linear function of the position the right side is a constant, and the path is a parabola,
//
//     x(sigma) = x0 + v0 sigma + 1/2 A sigma^2,     v(sigma) = v0 + A sigma,     A = 1/2 grad (n^2),
//
// exactly (Mo, Yeh and Manocha, "Tracing analytic ray curves for light and sound propagation in non-linear media", 2015).
// |v|^2 = n^2 holds along it, because both change at the rate 2 v . A.
//
// TetrahedralIndexField is the field for which that is so everywhere: n^2 is given at the points of a lattice, each cell
// of the lattice is cut into six tetrahedra, and n^2 is linear in each (the interpolation of its four corners). The field
// is continuous, so a path crosses from one tetrahedron into the next without a refraction; only the constant A changes.
// The crossing of a face is the root of a quadratic. There is no step size and no tolerance.
//
// The lattice may be finer than the data that the index comes from ("refine"): the index is then the data's own
// interpolation sampled at the lattice points, and the difference of this field from that interpolation falls with the
// square of the size of the cells, while the paths stay exact for this field.
//
// Cells whose eight corners have one index have no gradient, and a path through them is a straight line; the field knows
// how far every cell is from the nearest cell with a gradient, and a path crosses those regions in one piece.

using System;

namespace pbrt
{
    using static pbrt;

    // RefractivePiece Definition: one piece of a light path, a parabola (or a line) in sigma from 0 to `sigma`
    public struct RefractivePiece
    {
        // The position and the optical velocity at the start, and the constant A = 1/2 grad (n^2)
        public double x, y, z;
        public double vx, vy, vz;
        public double ax, ay, az;
        // The length of the piece in sigma
        public double sigma;
        // The largest and the smallest index over the tetrahedron of the piece (bounds of n along it: n^2 is linear there)
        public double largestIndex, smallestIndex;
        // Whether the path leaves the field at the end of the piece
        public bool leaves;

        public Point3f Point(double s)
        {
            return new Point3f((Float)(x + s * (vx + 0.5 * s * ax)), (Float)(y + s * (vy + 0.5 * s * ay)), (Float)(z + s * (vz + 0.5 * s * az)));
        }
    }

    // TetrahedralIndexField Definition
    public sealed unsafe class TetrahedralIndexField
    {
        // The field over a box, with n^2 linear in the tetrahedra of a lattice of cellsX x cellsY x cellsZ cells; the index
        // at the points of the lattice is what `index` gives there
        public TetrahedralIndexField(Bounds3f bounds, int cellsX, int cellsY, int cellsZ, Func<Point3f, double> index)
        {
            this.bounds = bounds;
            this.cellsX = cellsX;
            this.cellsY = cellsY;
            this.cellsZ = cellsZ;
            sizeX = (double)(bounds.pMax.x - bounds.pMin.x) / cellsX;
            sizeY = (double)(bounds.pMax.y - bounds.pMin.y) / cellsY;
            sizeZ = (double)(bounds.pMax.z - bounds.pMin.z) / cellsZ;
            squared = new double[(cellsX + 1) * (cellsY + 1) * (cellsZ + 1)];
            for (int k = 0; k <= cellsZ; ++k)
            {
                for (int j = 0; j <= cellsY; ++j)
                {
                    for (int i = 0; i <= cellsX; ++i)
                    {
                        Point3f p = new Point3f((Float)(bounds.pMin.x + i * sizeX), (Float)(bounds.pMin.y + j * sizeY), (Float)(bounds.pMin.z + k * sizeZ));
                        double n = index(p);
                        squared[Vertex(i, j, k)] = n * n;
                    }
                }
            }
            MeasureUniformCells();
        }

        // The same with nothing stored: the index at the points of the lattice is asked for when a path is there (for a
        // field too large to hold, as that of an atmosphere, given in closed form). `index` gives n at (x, y, z).
        public TetrahedralIndexField(Bounds3f bounds, int cellsX, int cellsY, int cellsZ, Func<double, double, double, double> index, bool asked)
        {
            this.bounds = bounds;
            this.cellsX = cellsX;
            this.cellsY = cellsY;
            this.cellsZ = cellsZ;
            sizeX = (double)(bounds.pMax.x - bounds.pMin.x) / cellsX;
            sizeY = (double)(bounds.pMax.y - bounds.pMin.y) / cellsY;
            sizeZ = (double)(bounds.pMax.z - bounds.pMin.z) / cellsZ;
            closedForm = index;
            skipUniform = false;
        }

        private double Squared(int i, int j, int k)
        {
            if (closedForm != null)
            {
                double n = closedForm(bounds.pMin.x + i * sizeX, bounds.pMin.y + j * sizeY, bounds.pMin.z + k * sizeZ);
                return 1 + (1 + dispersionShift) * (n * n - 1);
            }
            return 1 + (1 + dispersionShift) * (squared[Vertex(i, j, k)] - 1);
        }

        // Dispersion: the fields are those of one wavelength; for the paths of this thread n^2 - 1 is taken this much more
        // (a share of it, zero for none), as the index of a gas changes with the wavelength (RefractiveMedium.dispersion)
        [ThreadStatic]
        public static double dispersionShift;

        private Func<double, double, double, double> closedForm;

        // The field of the index given by a grid of samples, on a lattice `refine` times as fine as the grid. The samples
        // are at the centres of the grid's cells and interpolated as those of a density grid are (Interpolate), except that
        // beyond the outermost samples the index stays what it is there (a density is nothing outside its grid; an index
        // is not). An even `refine` puts the planes where that interpolation has its kinks on planes of the lattice.
        public static TetrahedralIndexField FromGrid(Bounds3f bounds, Float[] samples, int nx, int ny, int nz, int refine)
        {
            return FromGrid(bounds, samples, nx, ny, nz, refine, 0, 1);
        }

        // The same with the index offset + scale x sample, in double precision: for the index of a gas, which differs from
        // one in the fourth decimal and varies in the fifth (the samples are then the part that varies)
        public static TetrahedralIndexField FromGrid(Bounds3f bounds, Float[] samples, int nx, int ny, int nz, int refine, double offset, double scale)
        {
            return new TetrahedralIndexField(bounds, nx * refine, ny * refine, nz * refine, (Point3f p) =>
            {
                return offset + scale * Interpolate(samples, nx, ny, nz, (Point3f)bounds.Offset(p));
            });
        }

        // The samples of a grid interpolated at a point of the unit cube: trilinear between the eight nearest samples, with
        // the outermost samples continued outwards
        public static double Interpolate(Float[] samples, int nx, int ny, int nz, Point3f unit)
        {
            double sx = unit.x * nx - 0.5, sy = unit.y * ny - 0.5, sz = unit.z * nz - 0.5;
            int i = (int)Math.Floor(sx), j = (int)Math.Floor(sy), k = (int)Math.Floor(sz);
            double dx = sx - i, dy = sy - j, dz = sz - k;
            int i0 = Clamp(i, 0, nx - 1), i1 = Clamp(i + 1, 0, nx - 1), j0 = Clamp(j, 0, ny - 1), j1 = Clamp(j + 1, 0, ny - 1);
            int k0 = Clamp(k, 0, nz - 1), k1 = Clamp(k + 1, 0, nz - 1);
            double d00 = (1 - dx) * samples[i0 + nx * (j0 + ny * k0)] + dx * samples[i1 + nx * (j0 + ny * k0)];
            double d10 = (1 - dx) * samples[i0 + nx * (j1 + ny * k0)] + dx * samples[i1 + nx * (j1 + ny * k0)];
            double d01 = (1 - dx) * samples[i0 + nx * (j0 + ny * k1)] + dx * samples[i1 + nx * (j0 + ny * k1)];
            double d11 = (1 - dx) * samples[i0 + nx * (j1 + ny * k1)] + dx * samples[i1 + nx * (j1 + ny * k1)];
            return (1 - dz) * ((1 - dy) * d00 + dy * d10) + dz * ((1 - dy) * d01 + dy * d11);
        }

        // The index at a point of the box
        public Float Index(Point3f p)
        {
            double lx = (p.x - bounds.pMin.x) / sizeX, ly = (p.y - bounds.pMin.y) / sizeY, lz = (p.z - bounds.pMin.z) / sizeZ;
            int i = Clamp((int)Math.Floor(lx), 0, cellsX - 1), j = Clamp((int)Math.Floor(ly), 0, cellsY - 1), k = Clamp((int)Math.Floor(lz), 0, cellsZ - 1);
            double* local = stackalloc double[3];
            local[0] = Math.Min(Math.Max(lx - i, 0), 1);
            local[1] = Math.Min(Math.Max(ly - j, 0), 1);
            local[2] = Math.Min(Math.Max(lz - k, 0), 1);
            int first = 0, second = 1, third = 2;
            if (local[second] > local[first])
            {
                pstd.swap(ref first, ref second);
            }
            if (local[third] > local[second])
            {
                pstd.swap(ref second, ref third);
            }
            if (local[second] > local[first])
            {
                pstd.swap(ref first, ref second);
            }
            double* corner = stackalloc double[4];
            Corners(i, j, k, first, second, third, corner);
            double value = corner[0] + local[first] * (corner[1] - corner[0]) + local[second] * (corner[2] - corner[1]) + local[third] * (corner[3] - corner[2]);
            return (Float)Math.Sqrt(Math.Max(value, 0));
        }

        public Bounds3f Bounds() { return bounds; }

        // n^2 at the four corners of the tetrahedron of a cell where the local coordinate of the axis `first` is the
        // largest and that of `third` the smallest: the corner at the cell's origin, then one step along each axis in turn
        internal void Corners(int i, int j, int k, int first, int second, int third, double* corner)
        {
            int* at = stackalloc int[3];
            at[0] = i;
            at[1] = j;
            at[2] = k;
            corner[0] = Squared(at[0], at[1], at[2]);
            at[first]++;
            corner[1] = Squared(at[0], at[1], at[2]);
            at[second]++;
            corner[2] = Squared(at[0], at[1], at[2]);
            at[third]++;
            corner[3] = Squared(at[0], at[1], at[2]);
        }

        private int Vertex(int i, int j, int k)
        {
            return i + (cellsX + 1) * (j + (cellsY + 1) * k);
        }

        internal int Cell(int i, int j, int k)
        {
            return i + cellsX * (j + cellsY * k);
        }

        // For every cell, how many cells away the nearest cell with a gradient is (in the largest of the three index
        // differences; 0 for a cell with a gradient, at most 255): a path in a cell at the distance d crosses the block of
        // the cells within d - 1 of it in a straight line
        private void MeasureUniformCells()
        {
            uniformDistance = new byte[cellsX * cellsY * cellsZ];
            for (int k = 0; k < cellsZ; ++k)
            {
                for (int j = 0; j < cellsY; ++j)
                {
                    for (int i = 0; i < cellsX; ++i)
                    {
                        double value = squared[Vertex(i, j, k)];
                        bool uniform = true;
                        for (int c = 1; c < 8 && uniform; ++c)
                        {
                            uniform = squared[Vertex(i + (c & 1), j + ((c >> 1) & 1), k + ((c >> 2) & 1))] == value;
                        }
                        uniformDistance[Cell(i, j, k)] = uniform ? (byte)255 : (byte)0;
                    }
                }
            }
            // (the distance in the largest index difference, by a pass forwards and a pass backwards over the neighbours)
            for (int pass = 0; pass < 2; ++pass)
            {
                for (int n = 0; n < uniformDistance.Length; ++n)
                {
                    int cell = pass == 0 ? n : uniformDistance.Length - 1 - n;
                    int i = cell % cellsX, j = (cell / cellsX) % cellsY, k = cell / (cellsX * cellsY);
                    int nearest = uniformDistance[cell];
                    for (int dk = -1; dk <= 1; ++dk)
                    {
                        for (int dj = -1; dj <= 1; ++dj)
                        {
                            for (int di = -1; di <= 1; ++di)
                            {
                                int ni = i + di, nj = j + dj, nk = k + dk;
                                if (ni < 0 || nj < 0 || nk < 0 || ni >= cellsX || nj >= cellsY || nk >= cellsZ)
                                {
                                    continue;
                                }
                                nearest = Math.Min(nearest, uniformDistance[Cell(ni, nj, nk)] + 1);
                            }
                        }
                    }
                    uniformDistance[cell] = (byte)nearest;
                }
            }
        }

        internal Bounds3f bounds;
        internal int cellsX, cellsY, cellsZ;
        internal double sizeX, sizeY, sizeZ;
        internal double[] squared;
        internal byte[] uniformDistance;
        // Whether a path crosses the cells without a gradient in one piece (off: tetrahedron by tetrahedron, for the tests)
        public bool skipUniform = true;
    }

    // RefractiveRay Definition: a light path through a TetrahedralIndexField, piece by piece
    public sealed unsafe class RefractiveRay
    {
        // A path from a point of the field's box in a direction; false if the point is not in the box
        public bool Begin(TetrahedralIndexField field, Point3f p, Vector3f direction)
        {
            this.field = field;
            x = p.x;
            y = p.y;
            z = p.z;
            Vector3f d = Normalize(direction);
            // (the velocity has the length of the index: first along the direction with length one, to find the tetrahedron
            // that the path heads into, then with the index there)
            vx = d.x;
            vy = d.y;
            vz = d.z;
            entered = -1;
            inside = Locate();
            if (!inside)
            {
                return false;
            }
            double n = Math.Sqrt(Math.Max(SquaredHere(), 0));
            vx *= n;
            vy *= n;
            vz *= n;
            return true;
        }

        public bool Inside() { return inside; }

        // The path turned round where it is, to go back the way it came
        public void Reverse()
        {
            vx = -vx;
            vy = -vy;
            vz = -vz;
            entered = -1;
            inside = Locate();
        }

        // The next piece of the path, to the face of the tetrahedron (or of the block of cells without a gradient) that it
        // leaves through; the path is then at the end of the piece. False once the path has left the field.
        public bool Next(out RefractivePiece piece)
        {
            piece = default;
            if (!inside)
            {
                return false;
            }
            // (pieces of no length, at an edge or a corner, are stepped over)
            for (int guard = 0; guard < 4096; ++guard)
            {
                bool advanced = field.skipUniform && field.uniformDistance[field.Cell(ci, cj, ck)] > 0 ? Straight(ref piece) : Parabola(ref piece);
                if (advanced || !inside)
                {
                    piece.leaves = !inside;
                    return advanced || piece.leaves;
                }
            }
            inside = false;
            return false;
        }

        // n^2 at the path's point
        private double SquaredHere()
        {
            double* local = stackalloc double[3];
            Local(local);
            double* corner = stackalloc double[4];
            field.Corners(ci, cj, ck, first, second, third, corner);
            return corner[0] + local[first] * (corner[1] - corner[0]) + local[second] * (corner[2] - corner[1]) + local[third] * (corner[3] - corner[2]);
        }

        // The coordinates of the path's point in its cell, from 0 to 1
        private void Local(double* local)
        {
            local[0] = (x - field.bounds.pMin.x) / field.sizeX - ci;
            local[1] = (y - field.bounds.pMin.y) / field.sizeY - cj;
            local[2] = (z - field.bounds.pMin.z) / field.sizeZ - ck;
        }

        // The cell and the tetrahedron that the path is in or, on a face, heads into; false outside the field
        private bool Locate()
        {
            double* total = stackalloc double[3];
            double* rate = stackalloc double[3];
            int* cells = stackalloc int[3];
            int* cell = stackalloc int[3];
            double* local = stackalloc double[3];
            total[0] = (x - field.bounds.pMin.x) / field.sizeX;
            total[1] = (y - field.bounds.pMin.y) / field.sizeY;
            total[2] = (z - field.bounds.pMin.z) / field.sizeZ;
            rate[0] = vx / field.sizeX;
            rate[1] = vy / field.sizeY;
            rate[2] = vz / field.sizeZ;
            cells[0] = field.cellsX;
            cells[1] = field.cellsY;
            cells[2] = field.cellsZ;
            for (int a = 0; a < 3; ++a)
            {
                double nearest = Math.Round(total[a]);
                if (Math.Abs(total[a] - nearest) <= OnFace)
                {
                    // on a plane of the lattice: the cell on the side that the path goes to
                    cell[a] = rate[a] < 0 ? (int)nearest - 1 : (int)nearest;
                    local[a] = rate[a] < 0 ? 1 : 0;
                }
                else
                {
                    cell[a] = (int)Math.Floor(total[a]);
                    local[a] = total[a] - cell[a];
                }
                if (cell[a] < 0 || cell[a] >= cells[a])
                {
                    return false;
                }
            }
            ci = cell[0];
            cj = cell[1];
            ck = cell[2];
            // The order of the local coordinates, the largest first; equal ones by which grows faster
            first = 0;
            second = 1;
            third = 2;
            if (Before(local, rate, second, first))
            {
                pstd.swap(ref first, ref second);
            }
            if (Before(local, rate, third, second))
            {
                pstd.swap(ref second, ref third);
            }
            if (Before(local, rate, second, first))
            {
                pstd.swap(ref first, ref second);
            }
            return true;
        }

        private static bool Before(double* local, double* rate, int a, int b)
        {
            double difference = local[a] - local[b];
            if (Math.Abs(difference) > OnFace)
            {
                return difference > 0;
            }
            return rate[a] > rate[b];
        }

        // A piece through the tetrahedron that the path is in. False for a piece of no length (the path is then in the
        // next tetrahedron).
        private bool Parabola(ref RefractivePiece piece)
        {
            double* size = stackalloc double[3];
            double* local = stackalloc double[3];
            double* rate = stackalloc double[3];
            double* bend = stackalloc double[3];
            double* a = stackalloc double[3];
            double* corner = stackalloc double[4];
            size[0] = field.sizeX;
            size[1] = field.sizeY;
            size[2] = field.sizeZ;
            Local(local);
            field.Corners(ci, cj, ck, first, second, third, corner);
            // A = 1/2 grad (n^2): the differences of the corners are the gradient per cell along each axis
            a[first] = 0.5 * (corner[1] - corner[0]) / size[first];
            a[second] = 0.5 * (corner[2] - corner[1]) / size[second];
            a[third] = 0.5 * (corner[3] - corner[2]) / size[third];
            rate[0] = vx / size[0];
            rate[1] = vy / size[1];
            rate[2] = vz / size[2];
            for (int c = 0; c < 3; ++c)
            {
                bend[c] = a[c] / size[c];
            }
            // The four faces, each by how far inside it the path is (s >= 0), with the rate and the bend of that:
            //     0: the face of the cell where the first coordinate is 1       1: where the first equals the second
            //     2: where the second equals the third                           3: the face where the third is 0
            double* s = stackalloc double[4];
            double* p = stackalloc double[4];
            double* q = stackalloc double[4];
            s[0] = 1 - local[first];
            p[0] = -rate[first];
            q[0] = -bend[first];
            s[1] = local[first] - local[second];
            p[1] = rate[first] - rate[second];
            q[1] = bend[first] - bend[second];
            s[2] = local[second] - local[third];
            p[2] = rate[second] - rate[third];
            q[2] = bend[second] - bend[third];
            s[3] = local[third];
            p[3] = rate[third];
            q[3] = bend[third];
            double sigma = double.PositiveInfinity;
            int face = -1;
            for (int f = 0; f < 4; ++f)
            {
                double root = double.PositiveInfinity;
                if (f == entered || s[f] <= OnFace)
                {
                    // on the face: the path goes inwards and may come back to it, or goes out at once
                    if (p[f] > 0 || (p[f] == 0 && q[f] > 0))
                    {
                        if (q[f] < 0)
                        {
                            root = -2 * p[f] / q[f];
                        }
                    }
                    else if (p[f] < 0 || q[f] < 0)
                    {
                        root = 0;
                    }
                }
                else
                {
                    // the first time s + p sigma + 1/2 q sigma^2 falls to zero
                    double discriminant = p[f] * p[f] - 2 * q[f] * s[f];
                    if (discriminant >= 0)
                    {
                        double denominator = Math.Sqrt(discriminant) - p[f];
                        if (denominator > 0)
                        {
                            root = 2 * s[f] / denominator;
                        }
                    }
                }
                if (root < sigma)
                {
                    sigma = root;
                    face = f;
                }
            }
            if (face < 0)
            {
                // (a path that no face stops: there is none in a tetrahedron)
                inside = false;
                return false;
            }
            piece.x = x;
            piece.y = y;
            piece.z = z;
            piece.vx = vx;
            piece.vy = vy;
            piece.vz = vz;
            piece.ax = a[0];
            piece.ay = a[1];
            piece.az = a[2];
            piece.sigma = sigma;
            piece.largestIndex = Math.Sqrt(Math.Max(Math.Max(corner[0], corner[1]), Math.Max(corner[2], corner[3])));
            piece.smallestIndex = Math.Sqrt(Math.Max(Math.Min(Math.Min(corner[0], corner[1]), Math.Min(corner[2], corner[3])), 0));
            x += sigma * (vx + 0.5 * sigma * a[0]);
            y += sigma * (vy + 0.5 * sigma * a[1]);
            z += sigma * (vz + 0.5 * sigma * a[2]);
            vx += sigma * a[0];
            vy += sigma * a[1];
            vz += sigma * a[2];
            Cross(face);
            return sigma > 0;
        }

        // Into the tetrahedron on the other side of a face
        private void Cross(int face)
        {
            if (face == 1)
            {
                pstd.swap(ref first, ref second);
                entered = 1;
            }
            else if (face == 2)
            {
                pstd.swap(ref second, ref third);
                entered = 2;
            }
            else if (face == 0)
            {
                // the next cell along the first axis, where that coordinate is the smallest
                int axis = first;
                Step(axis, 1);
                first = second;
                second = third;
                third = axis;
                entered = 3;
            }
            else
            {
                // the cell before along the third axis, where that coordinate is the largest
                int axis = third;
                Step(axis, -1);
                third = second;
                second = first;
                first = axis;
                entered = 0;
            }
        }

        private void Step(int axis, int by)
        {
            if (axis == 0)
            {
                ci += by;
                inside = ci >= 0 && ci < field.cellsX;
            }
            else if (axis == 1)
            {
                cj += by;
                inside = cj >= 0 && cj < field.cellsY;
            }
            else
            {
                ck += by;
                inside = ck >= 0 && ck < field.cellsZ;
            }
        }

        // A straight piece through the block of cells without a gradient about the cell that the path is in
        private bool Straight(ref RefractivePiece piece)
        {
            int reach = field.uniformDistance[field.Cell(ci, cj, ck)] - 1;
            double* origin = stackalloc double[3];
            double* size = stackalloc double[3];
            double* position = stackalloc double[3];
            double* velocity = stackalloc double[3];
            int* cell = stackalloc int[3];
            int* cells = stackalloc int[3];
            origin[0] = field.bounds.pMin.x;
            origin[1] = field.bounds.pMin.y;
            origin[2] = field.bounds.pMin.z;
            size[0] = field.sizeX;
            size[1] = field.sizeY;
            size[2] = field.sizeZ;
            position[0] = x;
            position[1] = y;
            position[2] = z;
            velocity[0] = vx;
            velocity[1] = vy;
            velocity[2] = vz;
            cell[0] = ci;
            cell[1] = cj;
            cell[2] = ck;
            cells[0] = field.cellsX;
            cells[1] = field.cellsY;
            cells[2] = field.cellsZ;
            double sigma = double.PositiveInfinity;
            int axis = -1;
            for (int c = 0; c < 3; ++c)
            {
                if (velocity[c] == 0)
                {
                    continue;
                }
                int low = Math.Max(cell[c] - reach, 0), high = Math.Min(cell[c] + reach, cells[c] - 1);
                double plane = origin[c] + (velocity[c] > 0 ? high + 1 : low) * size[c];
                double root = Math.Max((plane - position[c]) / velocity[c], 0);
                if (root < sigma)
                {
                    sigma = root;
                    axis = c;
                }
            }
            if (axis < 0)
            {
                inside = false;
                return false;
            }
            piece.x = x;
            piece.y = y;
            piece.z = z;
            piece.vx = vx;
            piece.vy = vy;
            piece.vz = vz;
            piece.ax = 0;
            piece.ay = 0;
            piece.az = 0;
            piece.sigma = sigma;
            piece.largestIndex = Math.Sqrt(vx * vx + vy * vy + vz * vz);
            piece.smallestIndex = piece.largestIndex;
            x += sigma * vx;
            y += sigma * vy;
            z += sigma * vz;
            // (exactly on the plane that stopped it, so that the cell on the other side is found)
            int crossed = (velocity[axis] > 0 ? Math.Min(cell[axis] + reach, cells[axis] - 1) + 1 : Math.Max(cell[axis] - reach, 0));
            double onPlane = origin[axis] + crossed * size[axis];
            if (axis == 0)
            {
                x = onPlane;
            }
            else if (axis == 1)
            {
                y = onPlane;
            }
            else
            {
                z = onPlane;
            }
            entered = -1;
            inside = Locate();
            return sigma > 0;
        }

        // How near a face, in the units of a cell, a point counts as on it
        private const double OnFace = 1e-12;

        private TetrahedralIndexField field;
        // The position and the optical velocity
        public double x, y, z;
        public double vx, vy, vz;
        // The cell, and the order of its axes by the local coordinates (the largest first): the tetrahedron
        private int ci, cj, ck;
        private int first, second, third;
        // The face that the path came in through (-1: none known)
        private int entered;
        private bool inside;
    }
}
