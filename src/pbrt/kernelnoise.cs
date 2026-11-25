// (not in pbrt-v4) kernelnoise.cs
//
// Kernel noise: a sparse convolution noise of compact polynomial kernels. Space is a lattice of cells, and a cell holds, with the
// probability "coverage", one kernel: a point somewhere in the cell, a radius R of half a cell to a whole one, and the bump
//
//     K(r) = (1 - r^2 / R^2)^3    for r < R, nothing beyond,
//
// which is what the metaball of Wyvill, McPheeters and Wyvill ("Data Structure for Soft Objects", 1986) becomes when it is
// written in r^2. The noise is the sum of the kernels, and of octaves of them on finer lattices:
//
//     f(x) = sum_o gain^o sum_i K_i(|x - c_i|).
//
// It is made to be the density of a medium whose transmittance needs no estimate, after the cosine noise of Balint and Mantiuk
// (cosinenoise.cs), and for what that noise cannot do:
//
//     it is never negative     A sum of bumps. Nothing is clamped, so the closed form is the optical depth at every setting, and
//                              there is empty space wherever no kernel reaches ("coverage" below 1 opens it up).
//     its integral is exact    Along a ray r^2 is a quadratic in the distance, so a kernel is a polynomial of the sixth degree
//                              there and its integral one of the seventh. Measured from the point of the ray nearest the
//                              centre, at the distance b, with h^2 = R^2 - b^2:
//
//                                  integral of K ds = [h^6 s - h^4 s^3 + 3/5 h^2 s^5 - s^7 / 7] / R^6,    s within -h and h.
//
//     it is local              A point is reached by the kernels of its cell and of the cells around it, and a ray by those of
//                              the cells it passes: the cost of the integral follows the length of the ray in cells, and not
//                              the number of kernels there are.
//
// The integral grows with the distance, so the distance at which it reaches a given optical depth is one root, found by Newton's
// method inside a bracket: the distances to the collisions of a medium are sampled exactly, without a majorant and without a
// null collision. Nothing is tabulated: a cell's kernel comes from a hash of its coordinates.
//
// The density of the texture and of the medium is the noise over its mean, so that "density" is the mean density of a medium
// with every cell filled, and "coverage" times it otherwise.
//
// The shape. A sum of bumps is shaped by where the bumps are and how tall they are, and stays a sum of bumps: "string envelope"
// "cloud" gives each kernel the height that pbrt-v4's cloud medium has as its density at the kernel's centre, and the noise is
// that cloud, with the same exact integral (KernelNoiseCloudEnvelope). "blob" is a cloud of its own outline: a ball displaced by
// fBm (KernelNoiseBlobEnvelope).
//
//     KernelNoise              the function, its integral along a ray, the inverse of the integral, and its bounds over a box
//     KernelNoiseTexture       Texture "name" "float" "kernelnoise"
//     KernelNoiseMedium        MakeNamedMedium "name" "string type" "kernelnoise"

using System;

namespace pbrt
{
    using static pbrt;

    // KernelNoise Definition
    public sealed unsafe class KernelNoise
    {
        // KernelNoise Public Methods
        public KernelNoise(Float frequency, int octaves, Float lacunarity, Float gain, Float coverage, int seed, KernelNoiseEnvelope envelope)
        {
            this.envelope = envelope;
            this.octaves = octaves;
            this.coverage = coverage;
            this.seed = (ulong)(uint)seed;
            scale = new double[octaves];
            weight = new double[octaves];
            double octaveScale = frequency, octaveWeight = 1, weights = 0;
            for (int o = 0; o < octaves; ++o)
            {
                scale[o] = octaveScale;
                weight[o] = octaveWeight;
                weights += octaveWeight;
                octaveScale *= lacunarity;
                octaveWeight *= gain;
            }

            // The mean of an octave with every cell filled: the volume of a kernel, 64 pi / 315 R^3, by the mean of R^3 (R is 1/2
            // to 1 of a cell)
            double meanCube = (1.0 - 0.0625) / (4.0 * 0.5);
            mean = 64.0 * Math.PI / 315.0 * meanCube * weights;

            // The bound of an octave. A kernel is at most (1 - d^2)^3 at a point that is d cells from the cell it lies in, so the
            // sum of that over the cells around a part of a cell bounds the noise there; the largest over the parts of a cell
            // is the bound.
            int parts = 8;
            double cellBound = 0;
            for (int k = 0; k < parts; ++k)
            {
                for (int j = 0; j < parts; ++j)
                {
                    for (int i = 0; i < parts; ++i)
                    {
                        double sum = 0;
                        for (int dz = -1; dz <= 1; ++dz)
                        {
                            for (int dy = -1; dy <= 1; ++dy)
                            {
                                for (int dx = -1; dx <= 1; ++dx)
                                {
                                    double gx = Gap((double)i / parts, (double)(i + 1) / parts, dx);
                                    double gy = Gap((double)j / parts, (double)(j + 1) / parts, dy);
                                    double gz = Gap((double)k / parts, (double)(k + 1) / parts, dz);
                                    sum += Bump(1.0 - (gx * gx + gy * gy + gz * gz));
                                }
                            }
                        }
                        cellBound = Math.Max(cellBound, sum);
                    }
                }
            }
            octaveBound = cellBound;
            bound = cellBound * weights;
        }

        // Keeps the heights that the envelope gives the kernels of the cells about a box, each from the first time it is asked
        // for: the envelope is a good deal of Perlin noise, and a ray asks for the kernels of the same cells over and over. What
        // is kept is the value itself, so nothing changes but the time. (An octave of more than 2^24 cells there keeps nothing.)
        public void KeepHeights(Bounds3f box)
        {
            if (envelope == null)
            {
                return;
            }
            keptHeights = new float[octaves][];
            keptX = new long[octaves];
            keptY = new long[octaves];
            keptZ = new long[octaves];
            keptNx = new long[octaves];
            keptNy = new long[octaves];
            keptNz = new long[octaves];
            for (int o = 0; o < octaves; ++o)
            {
                keptX[o] = (long)Math.Floor(box.pMin.x * scale[o]) - 2;
                keptY[o] = (long)Math.Floor(box.pMin.y * scale[o]) - 2;
                keptZ[o] = (long)Math.Floor(box.pMin.z * scale[o]) - 2;
                keptNx[o] = (long)Math.Floor(box.pMax.x * scale[o]) + 3 - keptX[o];
                keptNy[o] = (long)Math.Floor(box.pMax.y * scale[o]) + 3 - keptY[o];
                keptNz[o] = (long)Math.Floor(box.pMax.z * scale[o]) + 3 - keptZ[o];
                double cells = (double)keptNx[o] * keptNy[o] * keptNz[o];
                if (cells > 1 << 24)
                {
                    continue;
                }
                keptHeights[o] = new float[(int)cells];
                Array.Fill(keptHeights[o], Single.NaN);
            }
        }

        // What the noise is divided by to give the density: its mean with every cell filled
        public double Mean()
        {
            return mean;
        }

        // A bound of the density
        public double MaxDensity()
        {
            return bound / mean;
        }

        // The noise at a point
        public double Field(double px, double py, double pz)
        {
            double sum = 0;
            for (int o = 0; o < octaves; ++o)
            {
                double qx = px * scale[o], qy = py * scale[o], qz = pz * scale[o];
                long ix = (long)Math.Floor(qx), iy = (long)Math.Floor(qy), iz = (long)Math.Floor(qz);
                double fx = qx - ix, fy = qy - iy, fz = qz - iz;
                double octave = 0;
                for (int dz = -1; dz <= 1; ++dz)
                {
                    for (int dy = -1; dy <= 1; ++dy)
                    {
                        for (int dx = -1; dx <= 1; ++dx)
                        {
                            // (a cell that is a whole cell away holds no kernel that reaches the point)
                            double gx = Gap(fx, fx, dx), gy = Gap(fy, fy, dy), gz = Gap(fz, fz, dz);
                            if (gx * gx + gy * gy + gz * gz >= 1)
                            {
                                continue;
                            }
                            double cx, cy, cz, radius, height;
                            if (!Kernel(o, ix + dx, iy + dy, iz + dz, out cx, out cy, out cz, out radius, out height))
                            {
                                continue;
                            }
                            double x = qx - cx, y = qy - cy, z = qz - cz;
                            octave += height * Bump(1.0 - (x * x + y * y + z * z) / (radius * radius));
                        }
                    }
                }
                sum += weight[o] * octave;
            }
            return sum;
        }

        public Float Density(Point3f p)
        {
            return (Float)(Field(p.x, p.y, p.z) / mean);
        }

        // The integral of the noise along the ray o + t d, for t from t0 to t1 (per unit of t)
        public double FieldIntegral(double ox, double oy, double oz, double dx, double dy, double dz, double t0, double t1)
        {
            int count = Gather(ox, oy, oz, dx, dy, dz, t0, t1);
            return GatheredIntegral(count, t0, t1);
        }

        // The distance t in (tStart, tEnd) at which the integral of the noise from tStart reaches the target. The noise is never
        // negative, so the integral grows with t and has one such distance: it is bracketed a cell of the coarsest octave at a
        // time, and found there by Newton's method kept inside the bracket (the derivative of the integral is the noise). The
        // kernels of a piece are gathered once, and the iterations work on their polynomials. False if the integral to tEnd,
        // returned in toEnd, is short of the target.
        public bool InvertFieldIntegral(double ox, double oy, double oz, double dx, double dy, double dz, double tStart, double tEnd, double target,
                                        out double t, out double toEnd)
        {
            double step = 1.0 / (scale[0] * Math.Sqrt(dx * dx + dy * dy + dz * dz));
            double lo = tStart, reached = 0;
            // (a ray without an end through cells that are all empty: no collision, after a great many of them)
            for (int cells = 0; cells < 1 << 20; ++cells)
            {
                double hi = Math.Min(tEnd, lo + step);
                int count = Gather(ox, oy, oz, dx, dy, dz, lo, hi);
                double piece = GatheredIntegral(count, lo, hi);
                if (reached + piece < target)
                {
                    reached += piece;
                    if (hi == tEnd)
                    {
                        break;
                    }
                    lo = hi;
                    continue;
                }
                // Within this piece
                double remaining = target - reached, start = lo;
                t = 0.5 * (lo + hi);
                for (int iteration = 0; iteration < 64; ++iteration)
                {
                    double value = GatheredIntegral(count, start, t) - remaining;
                    if (Math.Abs(value) <= 1e-9 * target || hi - lo <= 1e-13 * Math.Max(1.0, Math.Abs(hi)))
                    {
                        break;
                    }
                    if (value < 0)
                    {
                        lo = t;
                    }
                    else
                    {
                        hi = t;
                    }
                    double slope = GatheredValue(count, t);
                    double next = slope > 0 ? t - value / slope : lo;
                    t = next > lo && next < hi ? next : 0.5 * (lo + hi);
                }
                toEnd = 0;
                return true;
            }
            t = tEnd;
            toEnd = reached;
            return false;
        }

        // The smallest and the largest value of the noise over a box: every kernel that reaches the box at its value at the
        // point of the box farthest from its centre, and at the nearest. A box of a great many cells gets the bounds over all
        // of space.
        public void FieldBounds(Bounds3f box, out double lo, out double hi)
        {
            lo = 0;
            hi = 0;
            for (int o = 0; o < octaves; ++o)
            {
                double x0 = box.pMin.x * scale[o], y0 = box.pMin.y * scale[o], z0 = box.pMin.z * scale[o];
                double x1 = box.pMax.x * scale[o], y1 = box.pMax.y * scale[o], z1 = box.pMax.z * scale[o];
                long ix0 = (long)Math.Floor(x0) - 1, iy0 = (long)Math.Floor(y0) - 1, iz0 = (long)Math.Floor(z0) - 1;
                long ix1 = (long)Math.Floor(x1) + 1, iy1 = (long)Math.Floor(y1) + 1, iz1 = (long)Math.Floor(z1) + 1;
                if ((double)(ix1 - ix0 + 1) * (iy1 - iy0 + 1) * (iz1 - iz0 + 1) > 4096)
                {
                    hi += weight[o] * octaveBound;
                    continue;
                }
                double octaveLo = 0, octaveHi = 0;
                for (long iz = iz0; iz <= iz1; ++iz)
                {
                    for (long iy = iy0; iy <= iy1; ++iy)
                    {
                        for (long ix = ix0; ix <= ix1; ++ix)
                        {
                            double cx, cy, cz, radius, height;
                            if (!Kernel(o, ix, iy, iz, out cx, out cy, out cz, out radius, out height))
                            {
                                continue;
                            }
                            double nx = Math.Max(0.0, Math.Max(x0 - cx, cx - x1)), ny = Math.Max(0.0, Math.Max(y0 - cy, cy - y1));
                            double nz = Math.Max(0.0, Math.Max(z0 - cz, cz - z1));
                            double fx = Math.Max(cx - x0, x1 - cx), fy = Math.Max(cy - y0, y1 - cy), fz = Math.Max(cz - z0, z1 - cz);
                            octaveHi += height * Bump(1.0 - (nx * nx + ny * ny + nz * nz) / (radius * radius));
                            octaveLo += height * Bump(1.0 - (fx * fx + fy * fy + fz * fz) / (radius * radius));
                        }
                    }
                }
                lo += weight[o] * octaveLo;
                hi += weight[o] * Math.Min(octaveHi, octaveBound);
            }
        }

        // KernelNoise Private Methods
        // u^3 where u is positive: the kernel, of u = 1 - r^2 / R^2
        private static double Bump(double u)
        {
            return u > 0 ? u * u * u : 0;
        }

        // The distance, along one axis, from the part [lo, hi] of the cell [0, 1] to the cell that is d cells over
        private static double Gap(double lo, double hi, int d)
        {
            return d < 0 ? lo : d > 0 ? 1.0 - hi : 0;
        }

        // The kernel of a cell of an octave, if it has one: its centre, in cells, its radius and its height
        private bool Kernel(int octave, long ix, long iy, long iz, out double cx, out double cy, out double cz, out double radius, out double height)
        {
            // The height that was kept for the cell, if it is one of those of KeepHeights: nothing more to do for a kernel of no
            // height, and no envelope to ask for the others
            long slot = -1;
            float kept = Single.NaN;
            if (keptHeights != null && keptHeights[octave] != null)
            {
                long kx = ix - keptX[octave], ky = iy - keptY[octave], kz = iz - keptZ[octave];
                if (kx >= 0 && kx < keptNx[octave] && ky >= 0 && ky < keptNy[octave] && kz >= 0 && kz < keptNz[octave])
                {
                    slot = (kz * keptNy[octave] + ky) * keptNx[octave] + kx;
                    kept = keptHeights[octave][slot];
                    if (kept == 0)
                    {
                        cx = cy = cz = radius = height = 0;
                        return false;
                    }
                }
            }
            ulong h = MixBits((ulong)ix * 0x9E3779B97F4A7C15UL + seed);
            h = MixBits(h ^ ((ulong)iy * 0xC2B2AE3D27D4EB4FUL));
            h = MixBits(h ^ ((ulong)iz * 0x165667B19E3779F9UL) ^ ((ulong)octave << 56));
            if ((h >> 11) * TwoToMinus53 >= coverage)
            {
                cx = cy = cz = radius = height = 0;
                if (slot >= 0)
                {
                    keptHeights[octave][slot] = 0;
                }
                return false;
            }
            h = MixBits(h + 0x9E3779B97F4A7C15UL);
            cx = ix + (h >> 11) * TwoToMinus53;
            h = MixBits(h + 0x9E3779B97F4A7C15UL);
            cy = iy + (h >> 11) * TwoToMinus53;
            h = MixBits(h + 0x9E3779B97F4A7C15UL);
            cz = iz + (h >> 11) * TwoToMinus53;
            h = MixBits(h + 0x9E3779B97F4A7C15UL);
            radius = 0.5 + 0.5 * ((h >> 11) * TwoToMinus53);
            // The height of the bump: one, or what the envelope is at its centre
            height = 1;
            if (!Single.IsNaN(kept))
            {
                height = kept;
                return true;
            }
            if (envelope != null)
            {
                height = envelope.Evaluate(cx / scale[octave], cy / scale[octave], cz / scale[octave]);
                if (slot >= 0)
                {
                    keptHeights[octave][slot] = (float)height;
                }
                return height > 0;
            }
            return true;
        }

        // The kernels that reach the ray o + t d between t0 and t1, as four numbers each in the buffer of the thread: the t of the
        // point of the ray nearest the centre, the half chord h there (in cells), the speed of the ray in cells per unit of t,
        // and the weight of the octave over R^6
        private int Gather(double ox, double oy, double oz, double dx, double dy, double dz, double t0, double t1)
        {
            if (gathered == null)
            {
                gathered = new double[4 * 256];
            }
            int count = 0;
            for (int o = 0; o < octaves; ++o)
            {
                GatherOctave(o, ox * scale[o], oy * scale[o], oz * scale[o], dx * scale[o], dy * scale[o], dz * scale[o], t0, t1, ref count);
            }
            return count;
        }

        // The integral of the gathered kernels for t from t0 to t1: of each, with s the distance along the ray from its nearest
        // point, [h^6 s - h^4 s^3 + 3/5 h^2 s^5 - s^7 / 7] between the ends, kept within -h and h
        private static double GatheredIntegral(int count, double t0, double t1)
        {
            double[] kernels = gathered;
            double sum = 0;
            for (int i = 0; i < count; ++i)
            {
                double tc = kernels[4 * i], h = kernels[4 * i + 1], speed = kernels[4 * i + 2];
                double s0 = Math.Max(-h, speed * (t0 - tc)), s1 = Math.Min(h, speed * (t1 - tc));
                if (!(s1 > s0))
                {
                    continue;
                }
                double h2 = h * h, a0 = s0 * s0, a1 = s1 * s1;
                double p0 = s0 * (h2 * h2 * h2 - a0 * (h2 * h2 - a0 * (0.6 * h2 - a0 / 7.0)));
                double p1 = s1 * (h2 * h2 * h2 - a1 * (h2 * h2 - a1 * (0.6 * h2 - a1 / 7.0)));
                sum += kernels[4 * i + 3] * (p1 - p0) / speed;
            }
            return sum;
        }

        // The noise at the point t of the ray, from the gathered kernels
        private static double GatheredValue(int count, double t)
        {
            double[] kernels = gathered;
            double sum = 0;
            for (int i = 0; i < count; ++i)
            {
                double h = kernels[4 * i + 1], s = kernels[4 * i + 2] * (t - kernels[4 * i]);
                double u = h * h - s * s;
                if (u > 0)
                {
                    sum += kernels[4 * i + 3] * u * u * u;
                }
            }
            return sum;
        }

        // Adds the kernel of a cell if it reaches the ray q + t d (in cells) between t0 and t1
        private void AddKernel(int octave, long ix, long iy, long iz, double qx, double qy, double qz, double dx, double dy, double dz,
                               double speed, double t0, double t1, ref int count)
        {
            double cx, cy, cz, radius, height;
            if (!Kernel(octave, ix, iy, iz, out cx, out cy, out cz, out radius, out height))
            {
                return;
            }
            // The point of the ray nearest the centre, and half the chord of the kernel there
            double tc = ((cx - qx) * dx + (cy - qy) * dy + (cz - qz) * dz) / (speed * speed);
            double x = qx + tc * dx - cx, y = qy + tc * dy - cy, z = qz + tc * dz - cz;
            double h2 = radius * radius - (x * x + y * y + z * z);
            if (!(h2 > 0))
            {
                return;
            }
            double h = Math.Sqrt(h2);
            if (!(Math.Min(h, speed * (t1 - tc)) > Math.Max(-h, speed * (t0 - tc))))
            {
                return;
            }
            if (4 * count + 4 > gathered.Length)
            {
                Array.Resize(ref gathered, 2 * gathered.Length);
            }
            double r2 = radius * radius;
            gathered[4 * count] = tc;
            gathered[4 * count + 1] = h;
            gathered[4 * count + 2] = speed;
            gathered[4 * count + 3] = weight[octave] * height / (r2 * r2 * r2);
            ++count;
        }

        // The kernels of one octave that reach the ray between t0 and t1. They are those of the cells it passes and of the cells
        // around them: the cells around the first, and at each step into a next cell the nine beyond it. Each kernel is taken
        // once, over the whole of the ray.
        private void GatherOctave(int octave, double qx, double qy, double qz, double dx, double dy, double dz, double t0, double t1, ref int count)
        {
            double speed = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            long ix = (long)Math.Floor(qx + t0 * dx), iy = (long)Math.Floor(qy + t0 * dy), iz = (long)Math.Floor(qz + t0 * dz);
            for (int k = -1; k <= 1; ++k)
            {
                for (int j = -1; j <= 1; ++j)
                {
                    for (int i = -1; i <= 1; ++i)
                    {
                        AddKernel(octave, ix + i, iy + j, iz + k, qx, qy, qz, dx, dy, dz, speed, t0, t1, ref count);
                    }
                }
            }
            int stepX = dx > 0 ? 1 : -1, stepY = dy > 0 ? 1 : -1, stepZ = dz > 0 ? 1 : -1;
            while (true)
            {
                // Where the ray leaves the cell
                double nextX = dx != 0 ? (ix + (dx > 0 ? 1 : 0) - qx) / dx : Double.PositiveInfinity;
                double nextY = dy != 0 ? (iy + (dy > 0 ? 1 : 0) - qy) / dy : Double.PositiveInfinity;
                double nextZ = dz != 0 ? (iz + (dz > 0 ? 1 : 0) - qz) / dz : Double.PositiveInfinity;
                if (nextX <= nextY && nextX <= nextZ)
                {
                    if (!(nextX < t1))
                    {
                        break;
                    }
                    ix += stepX;
                    for (int k = -1; k <= 1; ++k)
                    {
                        for (int j = -1; j <= 1; ++j)
                        {
                            AddKernel(octave, ix + stepX, iy + j, iz + k, qx, qy, qz, dx, dy, dz, speed, t0, t1, ref count);
                        }
                    }
                }
                else if (nextY <= nextZ)
                {
                    if (!(nextY < t1))
                    {
                        break;
                    }
                    iy += stepY;
                    for (int k = -1; k <= 1; ++k)
                    {
                        for (int i = -1; i <= 1; ++i)
                        {
                            AddKernel(octave, ix + i, iy + stepY, iz + k, qx, qy, qz, dx, dy, dz, speed, t0, t1, ref count);
                        }
                    }
                }
                else
                {
                    if (!(nextZ < t1))
                    {
                        break;
                    }
                    iz += stepZ;
                    for (int j = -1; j <= 1; ++j)
                    {
                        for (int i = -1; i <= 1; ++i)
                        {
                            AddKernel(octave, ix + i, iy + j, iz + stepZ, qx, qy, qz, dx, dy, dz, speed, t0, t1, ref count);
                        }
                    }
                }
            }
        }

        // KernelNoise Private Members
        private const double TwoToMinus53 = 1.0 / 9007199254740992.0;
        [ThreadStatic]
        private static double[] gathered;
        private readonly int octaves;
        private readonly double coverage;
        private readonly ulong seed;
        private readonly double[] scale, weight;
        private readonly double mean, octaveBound, bound;
        private readonly KernelNoiseEnvelope envelope;
        // (the heights of the kernels about the box of KeepHeights, octave by octave; not a number where not yet asked for)
        private float[][] keptHeights;
        private long[] keptX, keptY, keptZ, keptNx, keptNy, keptNz;
    }

    // KernelNoiseEnvelope Definition: the height of the bump of a kernel, 0 to 1, from where its centre is (in the space of the
    // noise). The noise is shaped by it and stays a sum of bumps.
    public abstract class KernelNoiseEnvelope
    {
        public abstract double Evaluate(double x, double y, double z);
    }

    // KernelNoiseBlobEnvelope Definition
    //
    // A cloud of its own shape: a ball whose radius is displaced by fBm, R (1 + displacement fBm(frequency (p - centre) / R)),
    // full inside and falling to nothing across "edge" times R at its surface. The surface billows with the noise and the
    // medium ends there, well inside its box.
    public sealed class KernelNoiseBlobEnvelope : KernelNoiseEnvelope
    {
        public KernelNoiseBlobEnvelope(Point3f center, Float radius, Float displacement, Float frequency, Float edge)
        {
            this.center = center;
            this.radius = radius;
            this.displacement = displacement;
            this.frequency = frequency;
            this.edge = edge;
        }

        public override double Evaluate(double x, double y, double z)
        {
            Vector3f v = new Vector3f((Float)x - center.x, (Float)y - center.y, (Float)z - center.z);
            Float r = Length(v);
            // (beyond the reach of any displacement: no noise to ask for)
            Float reach = displacement * FBmBound(0.5f, 6);
            if (r >= radius * (1 + reach))
            {
                return 0;
            }
            Point3f q = new Point3f(frequency * v.x / radius, frequency * v.y / radius, frequency * v.z / radius);
            Float displaced = radius * (1 + displacement * FBm(q, new Vector3f(0, 0, 0), new Vector3f(0, 0, 0), 0.5f, 6));
            return Clamp((displaced - r) / (edge * radius), 0, 1);
        }

        private readonly Point3f center;
        private readonly Float radius, displacement, frequency, edge;
    }

    // KernelNoiseCloudEnvelope Definition
    //
    // The shape of a cloud for the kernel noise: the density of pbrt-v4's "cloud" medium (CloudMedium::Density), taken as the
    // height of the bump at each kernel's centre. That density is five octaves of Perlin noise about a warped point, scaled down
    // with altitude and clamped, which is what gives the cloud its outline and what no integral along a ray can be written for.
    // Here it is asked only at the centres of the kernels: the noise is then a sum of bumps of those heights, the cloud is where
    // the bumps are, and the integral along a ray is as exact as before. The point is in the space of the noise, with the cloud
    // between the altitudes 0 and 1.
    public sealed class KernelNoiseCloudEnvelope : KernelNoiseEnvelope
    {
        public KernelNoiseCloudEnvelope(Float frequency, Float wispiness, Float density)
        {
            this.frequency = frequency;
            this.wispiness = wispiness;
            this.density = density;
        }

        public override double Evaluate(double x, double y, double z)
        {
            Point3f p = new Point3f((Float)x, (Float)y, (Float)z);
            Point3f pp = frequency * p;
            if (wispiness > 0)
            {
                // Perturb cloud lookup point _pp_ using noise
                Float vomega = 0.05f * wispiness, vlambda = 10f;
                for (int i = 0; i < 2; ++i)
                {
                    pp += vomega * DNoise(vlambda * pp);
                    vomega *= 0.5f;
                    vlambda *= 1.99f;
                }
            }
            // Sum scales of noise to approximate cloud density
            Float d = 0;
            Float omega = 0.5f, lambda = 1f;
            for (int i = 0; i < 5; ++i)
            {
                d += omega * Noise(lambda * pp);
                omega *= 0.5f;
                lambda *= 1.99f;
            }

            // Model decrease in density with altitude and return final cloud density
            d = Clamp((1 - p.y) * 4.5f * density * d, 0, 1);
            d += 2 * Math.Max(0f, 0.5f - p.y);
            return Clamp(d, 0, 1);
        }

        private readonly Float frequency, wispiness, density;
    }

    // KernelNoiseTexture Definition
    //
    //     Texture "puffs" "float" "kernelnoise" "float frequency" 4 "integer octaves" 3 "float coverage" 0.6 "float density" 2
    //
    // "frequency" (1) is the number of cells of the first octave along a unit of length; "octaves" (3), "lacunarity" (2) and
    // "gain" (0.5) stack finer lattices; "coverage" (1) is the share of the cells that hold a kernel; "seed" (0) picks another
    // noise. The value is "density" (1) times the noise over its mean.
    public sealed unsafe class KernelNoiseTexture : FloatTexture
    {
        public KernelNoiseTexture(TextureMapping3D mapping, KernelNoise noise, Float density)
        {
            this.mapping = mapping;
            this.noise = noise;
            this.density = density;
        }

        public override Float MinValue()
        {
            return 0;
        }

        public override Float MaxValue()
        {
            return (Float)(density * noise.MaxDensity());
        }

        public override Float Evaluate(TextureEvalContext ctx)
        {
            Point3f p = mapping.Map(ctx).p;
            return density * noise.Density(p);
        }

        public override void RegionBounds(Bounds3f region, out Float lo, out Float hi)
        {
            // The box of the points of the texture that the region maps to
            Bounds3f box = new Bounds3f();
            for (int i = 0; i < 8; ++i)
            {
                TextureEvalContext ctx = new TextureEvalContext();
                ctx.p = region.Corner(i);
                Point3f corner = mapping.Map(ctx).p;
                box = (i == 0) ? new Bounds3f(corner) : pbrt.Union(box, corner);
            }
            double fieldLo, fieldHi;
            noise.FieldBounds(box, out fieldLo, out fieldHi);
            // (the arithmetic of the bounds and that of the value round differently)
            lo = (Float)Math.Max(0.0, density * fieldLo / noise.Mean() * (1 - 1e-5) - 1e-6);
            hi = (Float)Math.Min(density * noise.MaxDensity(), density * fieldHi / noise.Mean() * (1 + 1e-5) + 1e-6);
        }

        // "string envelope" is "none"; "cloud" for the shape of pbrt-v4's cloud medium, with that medium's "cloudfrequency" (5),
        // "wispiness" (1) and "clouddensity" (1); or "blob" for a ball displaced by fBm, about "blobcenter" (0.5 0.5 0.5) with
        // "blobradius" (0.3), "displacement" (0.6, as a share of the radius), "blobfrequency" (2, the fBm's across a radius) and
        // "edge" (0.25, the share of the radius across which the density falls to nothing)
        public static KernelNoise CreateNoise(Float frequency, int octaves, Float lacunarity, Float gain, Float coverage, int seed, string envelopeName,
                                              Float cloudFrequency, Float wispiness, Float cloudDensity, Point3f blobCenter, Float blobRadius,
                                              Float displacement, Float blobFrequency, Float edge, FileLoc loc)
        {
            if (envelopeName != "none" && envelopeName != "cloud" && envelopeName != "blob")
            {
                ErrorExit(loc, "\"kernelnoise\": \"envelope\" is \"none\", \"cloud\" or \"blob\".");
            }
            if (!(blobRadius > 0) || !(edge > 0) || displacement < 0)
            {
                ErrorExit(loc, "\"kernelnoise\": \"blobradius\" and \"edge\" are positive, and \"displacement\" is not negative.");
            }
            if (octaves < 1 || octaves > 16)
            {
                ErrorExit(loc, "\"kernelnoise\": \"octaves\" is 1 to 16.");
            }
            if (!(coverage > 0) || coverage > 1)
            {
                ErrorExit(loc, "\"kernelnoise\": \"coverage\" is above 0 and at most 1.");
            }
            if (!(frequency > 0) || !(lacunarity > 0) || !(gain > 0))
            {
                ErrorExit(loc, "\"kernelnoise\": \"frequency\", \"lacunarity\" and \"gain\" are positive.");
            }
            KernelNoiseEnvelope envelope = null;
            if (envelopeName == "cloud")
            {
                envelope = new KernelNoiseCloudEnvelope(cloudFrequency, wispiness, cloudDensity);
            }
            else if (envelopeName == "blob")
            {
                envelope = new KernelNoiseBlobEnvelope(blobCenter, blobRadius, displacement, blobFrequency, edge);
            }
            return new KernelNoise(frequency, octaves, lacunarity, gain, coverage, seed, envelope);
        }

        public static KernelNoiseTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters, FileLoc loc, Allocator alloc)
        {
            TextureMapping3D map = TextureMapping3D.Create(parameters, renderFromTexture, loc, alloc);
            KernelNoise noise = CreateNoise(parameters.GetOneFloat("frequency", 1), parameters.GetOneInt("octaves", 3), parameters.GetOneFloat("lacunarity", 2),
                                            parameters.GetOneFloat("gain", 0.5f), parameters.GetOneFloat("coverage", 1), parameters.GetOneInt("seed", 0),
                                            parameters.GetOneString("envelope", "none"), parameters.GetOneFloat("cloudfrequency", 5),
                                            parameters.GetOneFloat("wispiness", 1), parameters.GetOneFloat("clouddensity", 1),
                                            parameters.GetOnePoint3f("blobcenter", new Point3f(0.5f, 0.5f, 0.5f)), parameters.GetOneFloat("blobradius", 0.3f),
                                            parameters.GetOneFloat("displacement", 0.6f), parameters.GetOneFloat("blobfrequency", 2),
                                            parameters.GetOneFloat("edge", 0.25f), loc);
            return new KernelNoiseTexture(map, noise, parameters.GetOneFloat("density", 1));
        }

        public override string ToString()
        {
            return StringPrintf("[ KernelNoiseTexture mapping: %s density: %f ]", mapping, density);
        }

        private readonly TextureMapping3D mapping;
        private readonly KernelNoise noise;
        private readonly Float density;
    }

    // KernelNoiseMedium Definition
    //
    //     MakeNamedMedium "cloud" "string type" "kernelnoise" "spectrum sigma_a" ... "spectrum sigma_s" ... "float density" 2
    //         "float frequency" 4 "integer octaves" 3 "float coverage" 0.6
    //         "point3 p0" [...] "point3 p1" [...]
    //
    // The parameters of the noise are those of the texture. With "p0" and "p1" the medium fills that box of its own space, as the
    // "cloud" medium does; without them it fills the shape it is the interior of.
    //
    // Nothing of it is estimated. The transmittance is the closed form ("string transmittance" is "closedform" unless given), and
    // the distances to the collisions are sampled by inverting the closed form: every collision is a real one, and there is no
    // majorant. "string sampling" "majorant" tracks against bounds of the density instead (over the cells of a grid of
    // "majorantres" along each axis where there is a box, over all of space where there is none): it is there for comparison, and
    // for the trackers and the estimators of the volume integrator that work with majorants.
    public sealed unsafe class KernelNoiseMedium : Medium, IMajorantMedium<RayMajorantIterator>
    {
        public KernelNoiseMedium(bool bounded, Bounds3f bounds, Transform renderFromMedium, Spectrum sigma_a, Spectrum sigma_s, Float g, Float density,
                                 KernelNoise noise, bool closedFormSampling, int majorantResolution, Allocator alloc)
        {
            this.closedFormSampling = closedFormSampling;
            this.bounded = bounded;
            this.bounds = bounds;
            this.renderFromMedium = renderFromMedium;
            sigma_a_spec = new DenselySampledSpectrum(sigma_a, alloc);
            sigma_s_spec = new DenselySampledSpectrum(sigma_s, alloc);
            phase = new HGPhaseFunction(g, HGPhaseFunction.creationRayleigh);
            this.density = density;
            this.noise = noise;
            if (bounded)
            {
                noise.KeepHeights(bounds);
                // The largest density of each cell
                Point3i res = new Point3i(majorantResolution, majorantResolution, majorantResolution);
                densityGrid = new MajorantGrid(bounds, res, alloc);
                MajorantGrid densities = densityGrid;
                ParallelFor(0, res.z, (long z) =>
                {
                    for (int y = 0; y < res.y; ++y)
                    {
                        for (int x = 0; x < res.x; ++x)
                        {
                            Bounds3f unit = densities.VoxelBounds(x, y, (int)z);
                            Bounds3f cell = new Bounds3f(bounds.Lerp(unit.pMin), bounds.Lerp(unit.pMax));
                            double fieldLo, fieldHi;
                            noise.FieldBounds(cell, out fieldLo, out fieldHi);
                            densities.Set(x, y, (int)z, (Float)(fieldHi / noise.Mean() * (1 + 1e-5)));
                        }
                    }
                });
            }
        }

        public override string ToString()
        {
            return StringPrintf("[ KernelNoiseMedium bounds: %s renderFromMedium: %s phase: %s sigma_a_spec: %s sigma_s_spec: %s density: %f ]",
                                bounds, renderFromMedium, phase, sigma_a_spec, sigma_s_spec, density);
        }

        public override bool IsEmissive()
        {
            return false;
        }

        public override MediumProperties SamplePoint(Point3f p, SampledWavelengths lambda)
        {
            Float d = density * noise.Density(renderFromMedium.ApplyInverse(p));
            return new MediumProperties { sigma_a = d * sigma_a_spec.Sample(lambda), sigma_s = d * sigma_s_spec.Sample(lambda), phase = phase,
                                          Le = new SampledSpectrum(0f) };
        }

        // Whether the distances to the collisions are sampled from the closed form, without a majorant
        public bool ClosedFormSampling()
        {
            return closedFormSampling;
        }

        // SampleT_maj() from the closed form. The optical depth to the next collision of the wavelength that tracks is drawn,
        // -ln(1 - u) / sigma_t, and the distance at which the integral of the noise reaches it is found. The callback is given
        // the extinction of the medium at that point as the majorant and the transmittance to it, so every collision is a real
        // one, and the weights of the callers are those of a homogeneous medium, T sigma / (T[0] sigma_t[0]).
        public SampledSpectrum SampleT_maj(Ray ray, Float tMax, Float u, ref RNG rng, SampledWavelengths lambda,
                                           Func<Point3f, MediumProperties, SampledSpectrum, SampledSpectrum, bool> callback)
        {
            // Normalize ray direction and update _tMax_ accordingly
            tMax *= Length(ray.d);
            ray.d = Normalize(ray.d);

            Float t1 = tMax;
            Ray r = renderFromMedium.ApplyInverse(ray, &t1);
            double tStart = 0, tEnd = t1;
            if (bounded)
            {
                Float tMin, tLast;
                if (!bounds.IntersectP(r.o, r.d, t1, &tMin, &tLast))
                {
                    return new SampledSpectrum(1f);
                }
                tStart = tMin;
                tEnd = tLast;
            }
            SampledSpectrum sigma_t = density * (sigma_a_spec.Sample(lambda) + sigma_s_spec.Sample(lambda));
            if (!(tEnd > tStart))
            {
                return new SampledSpectrum(1f);
            }
            if (!(sigma_t[0] > 0))
            {
                // Nothing for the wavelength that tracks to collide with
                SampledSpectrum T_end = new SampledSpectrum(1f);
                double depth = IsInf(tEnd) ? Double.PositiveInfinity
                                           : noise.FieldIntegral(r.o.x, r.o.y, r.o.z, r.d.x, r.d.y, r.d.z, tStart, tEnd) / noise.Mean();
                for (int c = 0; c < NSpectrumSamples; ++c)
                {
                    T_end[c] = sigma_t[c] > 0 ? (Float)Math.Exp(-sigma_t[c] * depth) : 1f;
                }
                return T_end;
            }
            while (true)
            {
                // The optical depth to the collision, in units of the density, and the distance that has it
                double target = -Math.Log(1.0 - u) / sigma_t[0];
                double t, toEnd;
                bool collided = noise.InvertFieldIntegral(r.o.x, r.o.y, r.o.z, r.d.x, r.d.y, r.d.z, tStart, tEnd, target * noise.Mean(), out t, out toEnd);
                double depth = collided ? target : toEnd / noise.Mean();
                SampledSpectrum T_maj = new SampledSpectrum(1f);
                for (int c = 0; c < NSpectrumSamples; ++c)
                {
                    T_maj[c] = (Float)Math.Exp(-sigma_t[c] * depth);
                }
                if (!collided)
                {
                    return T_maj;
                }
                u = rng.Uniform<Float>();
                Point3f p = ray.Apply((Float)t);
                MediumProperties mp = SamplePoint(p, lambda, ray.time);
                SampledSpectrum sigma_maj = mp.sigma_a + mp.sigma_s;
                tStart = t;
                if (!(sigma_maj[0] > 0))
                {
                    // (the edge of a kernel, in single precision: of probability zero)
                    continue;
                }
                if (!callback(p, mp, sigma_maj, T_maj))
                {
                    return new SampledSpectrum(1f);
                }
            }
        }

        public RayMajorantIterator SampleRay(Ray ray, Float raytMax, SampledWavelengths lambda)
        {
            SampledSpectrum sigma_t = density * (sigma_a_spec.Sample(lambda) + sigma_s_spec.Sample(lambda));
            if (!bounded)
            {
                // One majorant: the bound of the density over all of space
                return new HomogeneousMajorantIterator(0, raytMax, (Float)noise.MaxDensity() * sigma_t);
            }
            ray = renderFromMedium.ApplyInverse(ray, &raytMax);
            Float tMin, tMax;
            if (!bounds.IntersectP(ray.o, ray.d, raytMax, &tMin, &tMax))
            {
                return new DDAMajorantIterator();
            }
            return new DDAMajorantIterator(ray, tMin, tMax, densityGrid, sigma_t);
        }

        public override RayMajorantIterator SampleRay(Ray ray, Float tMax, SampledWavelengths lambda, ScratchBuffer buf)
        {
            return SampleRay(ray, tMax, lambda);
        }

        // The transmittance of the segment of the ray up to tMax (in units of the length of ray.d), for "closedform": exact, and
        // the same number every time
        public override bool ClosedFormTransmittance(Ray ray, Float tMax, ref RNG rng, SampledWavelengths lambda, out SampledSpectrum T)
        {
            T = new SampledSpectrum(1f);
            double lengthPerT = Length(ray.d);
            Float t0 = 0, t1 = tMax;
            Ray r = renderFromMedium.ApplyInverse(ray, &t1);
            if (bounded)
            {
                Float tMin, tEnd;
                if (!bounds.IntersectP(r.o, r.d, t1, &tMin, &tEnd))
                {
                    return true;
                }
                t0 = tMin;
                t1 = tEnd;
            }
            if (IsInf(t1) || !(t1 > t0))
            {
                // (an endless segment has no finite optical depth to write down: the tracker handles it)
                return !IsInf(t1);
            }
            SampledSpectrum sigma_t = density * (sigma_a_spec.Sample(lambda) + sigma_s_spec.Sample(lambda));
            double depth = noise.FieldIntegral(r.o.x, r.o.y, r.o.z, r.d.x, r.d.y, r.d.z, t0, t1) / noise.Mean() * lengthPerT;
            for (int c = 0; c < NSpectrumSamples; ++c)
            {
                T[c] = (Float)Math.Exp(-sigma_t[c] * depth);
            }
            return true;
        }

        public override bool AggregateRegion(out Bounds3f mediumBounds, out Transform renderFromMedium, out MajorantGrid grid)
        {
            mediumBounds = bounds;
            renderFromMedium = this.renderFromMedium;
            grid = densityGrid;
            return bounded;
        }

        public override SampledSpectrum AggregateExtinction(SampledWavelengths lambda)
        {
            return (Float)(density * noise.MaxDensity()) * (sigma_a_spec.Sample(lambda) + sigma_s_spec.Sample(lambda));
        }

        public static KernelNoiseMedium Create(ParameterDictionary parameters, Transform renderFromMedium, FileLoc loc, Allocator alloc)
        {
            Float g = parameters.GetOneFloat("g", 0f);
            Spectrum sigma_a = parameters.GetOneSpectrum("sigma_a", null, SpectrumType.Unbounded, alloc);
            if (sigma_a == null)
            {
                sigma_a = new ConstantSpectrum(1f);
            }
            Spectrum sigma_s = parameters.GetOneSpectrum("sigma_s", null, SpectrumType.Unbounded, alloc);
            if (sigma_s == null)
            {
                sigma_s = new ConstantSpectrum(1f);
            }
            KernelNoise noise = KernelNoiseTexture.CreateNoise(parameters.GetOneFloat("frequency", 1), parameters.GetOneInt("octaves", 3),
                                                               parameters.GetOneFloat("lacunarity", 2), parameters.GetOneFloat("gain", 0.5f),
                                                               parameters.GetOneFloat("coverage", 1), parameters.GetOneInt("seed", 0),
                                                               parameters.GetOneString("envelope", "none"), parameters.GetOneFloat("cloudfrequency", 5),
                                                               parameters.GetOneFloat("wispiness", 1), parameters.GetOneFloat("clouddensity", 1),
                                            parameters.GetOnePoint3f("blobcenter", new Point3f(0.5f, 0.5f, 0.5f)), parameters.GetOneFloat("blobradius", 0.3f),
                                            parameters.GetOneFloat("displacement", 0.6f), parameters.GetOneFloat("blobfrequency", 2),
                                            parameters.GetOneFloat("edge", 0.25f), loc);
            // A box of the medium's own space, where one is given
            Point3f far = new Point3f(Infinity, Infinity, Infinity);
            Point3f p0 = parameters.GetOnePoint3f("p0", far);
            Point3f p1 = parameters.GetOnePoint3f("p1", far);
            bool bounded = !IsInf(p0.x) || !IsInf(p1.x);
            if (bounded && (IsInf(p0.x) || IsInf(p1.x)))
            {
                ErrorExit(loc, "\"kernelnoise\": \"p0\" and \"p1\" are given together.");
            }
            int majorantResolution = parameters.GetOneInt("majorantres", 64);
            if (majorantResolution < 1 || majorantResolution > 512)
            {
                ErrorExit(loc, "\"kernelnoise\": \"majorantres\" is 1 to 512.");
            }
            string sampling = parameters.GetOneString("sampling", "closedform");
            if (sampling != "closedform" && sampling != "majorant")
            {
                ErrorExit(loc, "\"kernelnoise\": \"sampling\" is \"closedform\" or \"majorant\".");
            }
            return new KernelNoiseMedium(bounded, bounded ? new Bounds3f(p0, p1) : new Bounds3f(), renderFromMedium, sigma_a, sigma_s, g,
                                         parameters.GetOneFloat("density", 1), noise, sampling == "closedform", majorantResolution, alloc);
        }

        // KernelNoiseMedium Private Members
        private readonly bool bounded, closedFormSampling;
        private readonly Bounds3f bounds;
        private readonly Transform renderFromMedium;
        private readonly HGPhaseFunction phase;
        private readonly DenselySampledSpectrum sigma_a_spec, sigma_s_spec;
        private readonly Float density;
        private readonly KernelNoise noise;
        private readonly MajorantGrid densityGrid;
    }
}
