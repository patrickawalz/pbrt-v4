// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// Magi: C# port of pbrt-v4. File: util/noise.h, util/noise.cpp

using System;

namespace pbrt
{
    public static unsafe partial class pbrt
    {
        // Perlin Noise Data
        private const int NoisePermSize = 256;
        private static readonly int[] NoisePerm = {
            151, 160, 137, 91, 90, 15, 131, 13, 201, 95, 96, 53, 194, 233, 7, 225,
            140, 36, 103, 30, 69, 142, 8, 99, 37, 240, 21, 10, 23, 190, 6, 148,
            247, 120, 234, 75, 0, 26, 197, 62, 94, 252, 219, 203, 117, 35, 11, 32,
            57, 177, 33, 88, 237, 149, 56, 87, 174, 20, 125, 136, 171, 168, 68, 175,
            74, 165, 71, 134, 139, 48, 27, 166, 77, 146, 158, 231, 83, 111, 229, 122,
            60, 211, 133, 230, 220, 105, 92, 41, 55, 46, 245, 40, 244, 102, 143, 54,
            65, 25, 63, 161, 1, 216, 80, 73, 209, 76, 132, 187, 208, 89, 18, 169,
            200, 196, 135, 130, 116, 188, 159, 86, 164, 100, 109, 198, 173, 186, 3, 64,
            52, 217, 226, 250, 124, 123, 5, 202, 38, 147, 118, 126, 255, 82, 85, 212,
            207, 206, 59, 227, 47, 16, 58, 17, 182, 189, 28, 42, 223, 183, 170, 213,
            119, 248, 152, 2, 44, 154, 163, 70, 221, 153, 101, 155, 167, 43, 172, 9,
            129, 22, 39, 253, 19, 98, 108, 110, 79, 113, 224, 232, 178, 185, 112, 104,
            218, 246, 97, 228, 251, 34, 242, 193, 238, 210, 144, 12, 191, 179, 162, 241,
            81, 51, 145, 235, 249, 14, 239, 107, 49, 192, 214, 31, 181, 199, 106, 157,
            184, 84, 204, 176, 115, 121, 50, 45, 127, 4, 150, 254, 138, 236, 205, 93,
            222, 114, 67, 29, 24, 72, 243, 141, 128, 195, 78, 66, 215, 61, 156, 180,
            151, 160, 137, 91, 90, 15, 131, 13, 201, 95, 96, 53, 194, 233, 7, 225,
            140, 36, 103, 30, 69, 142, 8, 99, 37, 240, 21, 10, 23, 190, 6, 148,
            247, 120, 234, 75, 0, 26, 197, 62, 94, 252, 219, 203, 117, 35, 11, 32,
            57, 177, 33, 88, 237, 149, 56, 87, 174, 20, 125, 136, 171, 168, 68, 175,
            74, 165, 71, 134, 139, 48, 27, 166, 77, 146, 158, 231, 83, 111, 229, 122,
            60, 211, 133, 230, 220, 105, 92, 41, 55, 46, 245, 40, 244, 102, 143, 54,
            65, 25, 63, 161, 1, 216, 80, 73, 209, 76, 132, 187, 208, 89, 18, 169,
            200, 196, 135, 130, 116, 188, 159, 86, 164, 100, 109, 198, 173, 186, 3, 64,
            52, 217, 226, 250, 124, 123, 5, 202, 38, 147, 118, 126, 255, 82, 85, 212,
            207, 206, 59, 227, 47, 16, 58, 17, 182, 189, 28, 42, 223, 183, 170, 213,
            119, 248, 152, 2, 44, 154, 163, 70, 221, 153, 101, 155, 167, 43, 172, 9,
            129, 22, 39, 253, 19, 98, 108, 110, 79, 113, 224, 232, 178, 185, 112, 104,
            218, 246, 97, 228, 251, 34, 242, 193, 238, 210, 144, 12, 191, 179, 162, 241,
            81, 51, 145, 235, 249, 14, 239, 107, 49, 192, 214, 31, 181, 199, 106, 157,
            184, 84, 204, 176, 115, 121, 50, 45, 127, 4, 150, 254, 138, 236, 205, 93,
            222, 114, 67, 29, 24, 72, 243, 141, 128, 195, 78, 66, 215, 61, 156, 180
        };

        // Noise Function Definitions
        public static Float Noise(Float x, Float y = .5f, Float z = .5f)
        {
            // Compute noise cell coordinates and offsets
            // Avoid overflow when computing deltas if the coordinates are too large to store in
            // int32s.
            x = std.fmod(x, (Float)(1 << 30));
            y = std.fmod(y, (Float)(1 << 30));
            z = std.fmod(z, (Float)(1 << 30));
            int ix = (int)std.floor(x), iy = (int)std.floor(y), iz = (int)std.floor(z);
            Float dx = x - ix, dy = y - iy, dz = z - iz;

            // Compute gradient weights
            ix &= NoisePermSize - 1;
            iy &= NoisePermSize - 1;
            iz &= NoisePermSize - 1;
            Float w000 = Grad(ix, iy, iz, dx, dy, dz);
            Float w100 = Grad(ix + 1, iy, iz, dx - 1, dy, dz);
            Float w010 = Grad(ix, iy + 1, iz, dx, dy - 1, dz);
            Float w110 = Grad(ix + 1, iy + 1, iz, dx - 1, dy - 1, dz);
            Float w001 = Grad(ix, iy, iz + 1, dx, dy, dz - 1);
            Float w101 = Grad(ix + 1, iy, iz + 1, dx - 1, dy, dz - 1);
            Float w011 = Grad(ix, iy + 1, iz + 1, dx, dy - 1, dz - 1);
            Float w111 = Grad(ix + 1, iy + 1, iz + 1, dx - 1, dy - 1, dz - 1);

            // Compute trilinear interpolation of weights
            Float wx = NoiseWeight(dx), wy = NoiseWeight(dy), wz = NoiseWeight(dz);
            Float x00 = Lerp(wx, w000, w100);
            Float x10 = Lerp(wx, w010, w110);
            Float x01 = Lerp(wx, w001, w101);
            Float x11 = Lerp(wx, w011, w111);
            Float y0 = Lerp(wy, x00, x10);
            Float y1 = Lerp(wy, x01, x11);
            return Lerp(wz, y0, y1);
        }

        public static Float Noise(Point3f p)
        {
            return Noise(p.x, p.y, p.z);
        }

        private static Float Grad(int x, int y, int z, Float dx, Float dy, Float dz)
        {
            int h = NoisePerm[NoisePerm[NoisePerm[x] + y] + z];
            h &= 15;
            Float u = h < 8 || h == 12 || h == 13 ? dx : dy;
            Float v = h < 4 || h == 12 || h == 13 ? dy : dz;
            return (((h & 1) != 0) ? -u : u) + (((h & 2) != 0) ? -v : v);
        }

        private static Float NoiseWeight(Float t)
        {
            return 6 * Pow5(t) - 15 * Pow4(t) + 10 * Pow3(t);
        }

        public static Vector3f DNoise(Point3f p)
        {
            Float delta = .01f;
            Float n = Noise(p);
            Point3f noiseDelta = new Point3f(Noise(p + new Vector3f(delta, 0, 0)), Noise(p + new Vector3f(0, delta, 0)),
                                             Noise(p + new Vector3f(0, 0, delta)));
            return (noiseDelta - new Point3f(n, n, n)) / delta;
        }

        public static Float FBm(Point3f p, Vector3f dpdx, Vector3f dpdy, Float omega, int maxOctaves)
        {
            // Compute number of octaves for antialiased FBm
            Float len2 = std.max(LengthSquared(dpdx), LengthSquared(dpdy));
            Float n = Clamp(-1 - Log2(len2) / 2, 0, maxOctaves);
            int nInt = (int)std.floor(n);

            // Compute sum of octaves of noise for FBm
            Float sum = 0, lambda = 1, o = 1;
            for (int i = 0; i < nInt; ++i)
            {
                sum += o * Noise(lambda * p);
                lambda *= 1.99f;
                o *= omega;
            }
            Float nPartial = n - nInt;
            sum += o * SmoothStep(nPartial, .3f, .7f) * Noise(lambda * p);

            return sum;
        }

        // (not in pbrt-v4) Bound on |Noise()|; verified numerically in noise_test.cs.
        public const Float NoiseBound = 1;

        // (not in pbrt-v4) Bound on |FBm()| for any p: at most maxOctaves terms of weight omega^i
        public static Float FBmBound(Float omega, int maxOctaves)
        {
            Float sum = 0, o = 1;
            for (int i = 0; i < maxOctaves; ++i)
            {
                sum += o;
                o *= std.abs(omega);
            }
            return NoiseBound * sum;
        }

        // (not in pbrt-v4) Upper bound on Turbulence(): the worst case over the number of full octaves
        // nInt of nInt noise terms, the partial term at index nInt (|Noise()| or 0.2) and the 0.2 terms
        // that stand in for the remaining octaves.
        public static Float TurbulenceBound(Float omega, int maxOctaves)
        {
            Float worst = 0;
            for (int nInt = 0; nInt <= maxOctaves; ++nInt)
            {
                Float sum = 0, o = 1;
                for (int i = 0; i < nInt; ++i)
                {
                    sum += o * NoiseBound;
                    o *= std.abs(omega);
                }
                sum += o * std.max(NoiseBound, (Float)0.2);
                for (int i = nInt; i < maxOctaves; ++i)
                {
                    sum += o * 0.2f;
                    o *= std.abs(omega);
                }
                worst = std.max(worst, sum);
            }
            return worst;
        }

        // (not in pbrt-v4) Rigorous bounds on Noise() over a box. Noise() is a fixed function of the
        // gradients at the corners of each unit lattice cell, so the box is clipped to each lattice cell
        // it overlaps and the cell's trilinear-of-fade-weights expression is evaluated in interval
        // arithmetic. Boxes spanning more than three cells along an axis just get [-NoiseBound, NoiseBound].
        public static void NoiseBounds(Bounds3f box, out Float lo, out Float hi)
        {
            lo = -NoiseBound;
            hi = NoiseBound;
            if (std.abs(box.pMin.x) > 1e8f || std.abs(box.pMax.x) > 1e8f || std.abs(box.pMin.y) > 1e8f ||
                std.abs(box.pMax.y) > 1e8f || std.abs(box.pMin.z) > 1e8f || std.abs(box.pMax.z) > 1e8f)
                return;
            int x0 = (int)std.floor(box.pMin.x), x1 = (int)std.floor(box.pMax.x);
            int y0 = (int)std.floor(box.pMin.y), y1 = (int)std.floor(box.pMax.y);
            int z0 = (int)std.floor(box.pMin.z), z1 = (int)std.floor(box.pMax.z);
            if (x1 - x0 > 2 || y1 - y0 > 2 || z1 - z0 > 2)
                return;

            Float cellLo = Infinity, cellHi = -Infinity;
            for (int iz = z0; iz <= z1; ++iz)
            {
                for (int iy = y0; iy <= y1; ++iy)
                {
                    for (int ix = x0; ix <= x1; ++ix)
                    {
                        // Offsets of the box within this lattice cell
                        Float ax = Clamp(box.pMin.x - ix, 0, 1), bx = Clamp(box.pMax.x - ix, 0, 1);
                        Float ay = Clamp(box.pMin.y - iy, 0, 1), by = Clamp(box.pMax.y - iy, 0, 1);
                        Float az = Clamp(box.pMin.z - iz, 0, 1), bz = Clamp(box.pMax.z - iz, 0, 1);
                        NoiseCellBounds(ix & (NoisePermSize - 1), iy & (NoisePermSize - 1), iz & (NoisePermSize - 1),
                                        ax, bx, ay, by, az, bz, out Float l, out Float h);
                        cellLo = std.min(cellLo, l);
                        cellHi = std.max(cellHi, h);
                    }
                }
            }
            // widen slightly for floating-point rounding in Noise()
            lo = std.max(-NoiseBound, cellLo - 2e-5f);
            hi = std.min(NoiseBound, cellHi + 2e-5f);
        }

        // Interval of Grad() for a corner whose offsets lie in [dx0,dx1] x [dy0,dy1] x [dz0,dz1]
        private static void GradBounds(int x, int y, int z, Float dx0, Float dx1, Float dy0, Float dy1, Float dz0,
                                       Float dz1, out Float lo, out Float hi)
        {
            int h = NoisePerm[NoisePerm[NoisePerm[x] + y] + z];
            h &= 15;
            Float u0 = h < 8 || h == 12 || h == 13 ? dx0 : dy0, u1 = h < 8 || h == 12 || h == 13 ? dx1 : dy1;
            Float v0 = h < 4 || h == 12 || h == 13 ? dy0 : dz0, v1 = h < 4 || h == 12 || h == 13 ? dy1 : dz1;
            if ((h & 1) != 0)
            {
                Float t = -u0;
                u0 = -u1;
                u1 = t;
            }
            if ((h & 2) != 0)
            {
                Float t = -v0;
                v0 = -v1;
                v1 = t;
            }
            lo = u0 + v0;
            hi = u1 + v1;
        }

        // Interval of (1 - t) * a + t * b with t in [t0, t1] subset of [0, 1] and a in [a0, a1], b in [b0, b1]
        private static void LerpBounds(Float t0, Float t1, Float a0, Float a1, Float b0, Float b1, out Float lo,
                                       out Float hi)
        {
            // (1 - t) in [1 - t1, 1 - t0], both factors nonnegative
            Float p0 = std.min(std.min((1 - t1) * a0, (1 - t1) * a1), std.min((1 - t0) * a0, (1 - t0) * a1));
            Float p1 = std.max(std.max((1 - t1) * a0, (1 - t1) * a1), std.max((1 - t0) * a0, (1 - t0) * a1));
            Float q0 = std.min(std.min(t0 * b0, t0 * b1), std.min(t1 * b0, t1 * b1));
            Float q1 = std.max(std.max(t0 * b0, t0 * b1), std.max(t1 * b0, t1 * b1));
            lo = p0 + q0;
            hi = p1 + q1;
        }

        private static void NoiseCellBounds(int ix, int iy, int iz, Float ax, Float bx, Float ay, Float by, Float az,
                                            Float bz, out Float lo, out Float hi)
        {
            GradBounds(ix, iy, iz, ax, bx, ay, by, az, bz, out Float l000, out Float h000);
            GradBounds(ix + 1, iy, iz, ax - 1, bx - 1, ay, by, az, bz, out Float l100, out Float h100);
            GradBounds(ix, iy + 1, iz, ax, bx, ay - 1, by - 1, az, bz, out Float l010, out Float h010);
            GradBounds(ix + 1, iy + 1, iz, ax - 1, bx - 1, ay - 1, by - 1, az, bz, out Float l110, out Float h110);
            GradBounds(ix, iy, iz + 1, ax, bx, ay, by, az - 1, bz - 1, out Float l001, out Float h001);
            GradBounds(ix + 1, iy, iz + 1, ax - 1, bx - 1, ay, by, az - 1, bz - 1, out Float l101, out Float h101);
            GradBounds(ix, iy + 1, iz + 1, ax, bx, ay - 1, by - 1, az - 1, bz - 1, out Float l011, out Float h011);
            GradBounds(ix + 1, iy + 1, iz + 1, ax - 1, bx - 1, ay - 1, by - 1, az - 1, bz - 1, out Float l111,
                       out Float h111);

            // NoiseWeight() is increasing on [0, 1]
            Float wx0 = NoiseWeight(ax), wx1 = NoiseWeight(bx);
            Float wy0 = NoiseWeight(ay), wy1 = NoiseWeight(by);
            Float wz0 = NoiseWeight(az), wz1 = NoiseWeight(bz);

            LerpBounds(wx0, wx1, l000, h000, l100, h100, out Float lx00, out Float hx00);
            LerpBounds(wx0, wx1, l010, h010, l110, h110, out Float lx10, out Float hx10);
            LerpBounds(wx0, wx1, l001, h001, l101, h101, out Float lx01, out Float hx01);
            LerpBounds(wx0, wx1, l011, h011, l111, h111, out Float lx11, out Float hx11);
            LerpBounds(wy0, wy1, lx00, hx00, lx10, hx10, out Float ly0, out Float hy0);
            LerpBounds(wy0, wy1, lx01, hx01, lx11, hx11, out Float ly1, out Float hy1);
            LerpBounds(wz0, wz1, ly0, hy0, ly1, hy1, out lo, out hi);
        }

        // (not in pbrt-v4) Bounds on FBm() over a box (in the space passed to FBm) with zero ray
        // differentials: the sum of each octave's NoiseBounds() over the scaled box.
        public static void FBmBounds(Bounds3f box, Float omega, int octaves, out Float lo, out Float hi)
        {
            lo = 0; hi = 0;
            Float lambda = 1, o = 1;
            for (int i = 0; i < octaves; ++i)
            {
                Bounds3f scaled = new Bounds3f(lambda * box.pMin, lambda * box.pMax);
                NoiseBounds(scaled, out Float termLo, out Float termHi);
                if (o >= 0)
                {
                    lo += o * termLo;
                    hi += o * termHi;
                }
                else
                {
                    lo += o * termHi;
                    hi += o * termLo;
                }
                lambda *= 1.99f;
                o *= omega;
            }
        }

        // (not in pbrt-v4) min over a in [a0,a1], b in [b0,b1], t in [t0,t1] of (1 - t) * a + t * b.
        // The expression is multilinear, so the extremes are at the corners.
        public static Float BlendMin(Float a0, Float a1, Float b0, Float b1, Float t0, Float t1)
        {
            Float m = Infinity;
            for (int ia = 0; ia < 2; ++ia)
            {
                for (int ib = 0; ib < 2; ++ib)
                {
                    for (int it = 0; it < 2; ++it)
                    {
                        Float a = ia == 0 ? a0 : a1, b = ib == 0 ? b0 : b1, t = it == 0 ? t0 : t1;
                        Float v = (1 - t) * a + t * b;
                        if (Float.IsNaN(v))
                            return -Infinity;
                        m = std.min(m, v);
                    }
                }
            }
            return m;
        }

        // (not in pbrt-v4) min over a in [a0,a1], b in [b0,b1] of a * b
        public static Float ProductMin(Float a0, Float a1, Float b0, Float b1)
        {
            Float v0 = a0 * b0, v1 = a0 * b1, v2 = a1 * b0, v3 = a1 * b1;
            if (Float.IsNaN(v0) || Float.IsNaN(v1) || Float.IsNaN(v2) || Float.IsNaN(v3))
                return -Infinity;
            return std.min(std.min(v0, v1), std.min(v2, v3));
        }

        public static Float Turbulence(Point3f p, Vector3f dpdx, Vector3f dpdy, Float omega, int maxOctaves)
        {
            // Compute number of octaves for antialiased FBm
            Float len2 = std.max(LengthSquared(dpdx), LengthSquared(dpdy));
            Float n = Clamp(-1 - Log2(len2) / 2, 0, maxOctaves);
            int nInt = (int)std.floor(n);

            // Compute sum of octaves of noise for turbulence
            Float sum = 0, lambda = 1, o = 1;
            for (int i = 0; i < nInt; ++i)
            {
                sum += o * std.abs(Noise(lambda * p));
                lambda *= 1.99f;
                o *= omega;
            }

            // Account for contributions of clamped octaves in turbulence
            Float nPartial = n - nInt;
            sum += o * Lerp(SmoothStep(nPartial, .3f, .7f), (Float)0.2, std.abs(Noise(lambda * p)));
            for (int i = nInt; i < maxOctaves; ++i)
            {
                sum += o * 0.2f;
                o *= omega;
            }

            return sum;
        }
    }
}
