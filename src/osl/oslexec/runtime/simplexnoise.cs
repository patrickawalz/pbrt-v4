// OSL port: src/liboslnoise/simplexnoise.cpp
//
// The simplex noise code was adapted from code by Stefan Gustavson (public domain), heavily modified for OSL by Larry Gritz: the permutation tables are
// replaced by a hash, 'seed' parameters give each component of the vector versions a different noise, and the analytic derivative is optional.
// The gradient lookups return a pointer into a table in the C++; here they return the components as out parameters (zero when the corner does not
// contribute, which is what the derivative code relies on).

using System;
using System.Runtime.CompilerServices;
using OIIO;

namespace OSL
{
    namespace pvt
    {
        public static unsafe class SimplexNoiseFuncs
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static uint scramble(uint v0, uint v1, uint v2)
            {
                return bjhash.bjfinal(v0, v1, v2 ^ 0xdeadbeefu);
            }

            // Gradient table for 2D. These could be programmed the Ken Perlin way with some clever bit-twiddling, but this is more clear, and not really slower.
            private static readonly float[] grad2lut = new float[]
            {
                -1.0f, -1.0f, 1.0f, 0.0f, -1.0f, 0.0f, 1.0f, 1.0f,
                -1.0f, 1.0f, 0.0f, -1.0f, 0.0f, 1.0f, 1.0f, -1.0f
            };

            // Gradient directions for 3D. These vectors are based on the midpoints of the 12 edges of a cube (with 4 repeats to make the array length 16).
            private static readonly float[] grad3lut = new float[]
            {
                1.0f, 0.0f, 1.0f, 0.0f, 1.0f, 1.0f, // 12 cube edges
                -1.0f, 0.0f, 1.0f, 0.0f, -1.0f, 1.0f,
                1.0f, 0.0f, -1.0f, 0.0f, 1.0f, -1.0f,
                -1.0f, 0.0f, -1.0f, 0.0f, -1.0f, -1.0f,
                1.0f, -1.0f, 0.0f, 1.0f, 1.0f, 0.0f,
                -1.0f, 1.0f, 0.0f, -1.0f, -1.0f, 0.0f,
                1.0f, 0.0f, 1.0f, -1.0f, 0.0f, 1.0f, // 4 repeats to make 16
                0.0f, 1.0f, -1.0f, 0.0f, -1.0f, -1.0f
            };

            // Gradient directions for 4D
            private static readonly float[] grad4lut = new float[]
            {
                0.0f, 1.0f, 1.0f, 1.0f, 0.0f, 1.0f, 1.0f, -1.0f, 0.0f, 1.0f, -1.0f, 1.0f, 0.0f, 1.0f, -1.0f, -1.0f, // 32 tesseract edges
                0.0f, -1.0f, 1.0f, 1.0f, 0.0f, -1.0f, 1.0f, -1.0f, 0.0f, -1.0f, -1.0f, 1.0f, 0.0f, -1.0f, -1.0f, -1.0f,
                1.0f, 0.0f, 1.0f, 1.0f, 1.0f, 0.0f, 1.0f, -1.0f, 1.0f, 0.0f, -1.0f, 1.0f, 1.0f, 0.0f, -1.0f, -1.0f,
                -1.0f, 0.0f, 1.0f, 1.0f, -1.0f, 0.0f, 1.0f, -1.0f, -1.0f, 0.0f, -1.0f, 1.0f, -1.0f, 0.0f, -1.0f, -1.0f,
                1.0f, 1.0f, 0.0f, 1.0f, 1.0f, 1.0f, 0.0f, -1.0f, 1.0f, -1.0f, 0.0f, 1.0f, 1.0f, -1.0f, 0.0f, -1.0f,
                -1.0f, 1.0f, 0.0f, 1.0f, -1.0f, 1.0f, 0.0f, -1.0f, -1.0f, -1.0f, 0.0f, 1.0f, -1.0f, -1.0f, 0.0f, -1.0f,
                1.0f, 1.0f, 1.0f, 0.0f, 1.0f, 1.0f, -1.0f, 0.0f, 1.0f, -1.0f, 1.0f, 0.0f, 1.0f, -1.0f, -1.0f, 0.0f,
                -1.0f, 1.0f, 1.0f, 0.0f, -1.0f, 1.0f, -1.0f, 0.0f, -1.0f, -1.0f, 1.0f, 0.0f, -1.0f, -1.0f, -1.0f, 0.0f
            };

            // A lookup table to traverse the simplex around a given point in 4D.
            private static readonly byte[] simplex = new byte[]
            {
                0, 1, 2, 3, 0, 1, 3, 2, 0, 0, 0, 0, 0, 2, 3, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 2, 3, 0,
                0, 2, 1, 3, 0, 0, 0, 0, 0, 3, 1, 2, 0, 3, 2, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 3, 2, 0,
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                1, 2, 0, 3, 0, 0, 0, 0, 1, 3, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 3, 0, 1, 2, 3, 1, 0,
                1, 0, 2, 3, 1, 0, 3, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 3, 1, 0, 0, 0, 0, 2, 1, 3, 0,
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                2, 0, 1, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 3, 0, 1, 2, 3, 0, 2, 1, 0, 0, 0, 0, 3, 1, 2, 0,
                2, 1, 0, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 3, 1, 0, 2, 0, 0, 0, 0, 3, 2, 0, 1, 3, 2, 1, 0
            };

            // Helper functions to compute gradients in 1D to 4D
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static float grad1(int i, int seed)
            {
                int h = (int)scramble((uint)i, (uint)seed, 0u);
                float g = 1.0f + (h & 7); // Gradient value is one of 1.0, 2.0, ..., 8.0
                if ((h & 8) != 0)
                {
                    g = -g; // Make half of the gradients negative
                }
                return g;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static void grad2(int i, int j, int seed, out float g0, out float g1)
            {
                int h = (int)scramble((uint)i, (uint)j, (uint)seed);
                int o = (h & 7) * 2;
                g0 = grad2lut[o];
                g1 = grad2lut[o + 1];
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static void grad3(int i, int j, int k, int seed, out float g0, out float g1, out float g2)
            {
                int h = (int)scramble((uint)i, (uint)j, scramble((uint)k, (uint)seed, 0u));
                int o = (h & 15) * 3;
                g0 = grad3lut[o];
                g1 = grad3lut[o + 1];
                g2 = grad3lut[o + 2];
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static void grad4(int i, int j, int k, int l, int seed, out float g0, out float g1, out float g2, out float g3)
            {
                int h = (int)scramble((uint)i, (uint)j, scramble((uint)k, (uint)l, (uint)seed));
                int o = (h & 31) * 4;
                g0 = grad4lut[o];
                g1 = grad4lut[o + 1];
                g2 = grad4lut[o + 2];
                g3 = grad4lut[o + 3];
            }

            // 1D simplex noise with derivative.
            // If the last argument is not null, the analytic derivative is also calculated.
            public static float simplexnoise1(float x, int seed = 0, float* dnoise_dx = null)
            {
                int i0 = fmath.ifloor(x);
                int i1 = i0 + 1;
                float x0 = x - i0;
                float x1 = x0 - 1.0f;

                float x20 = x0 * x0;
                float t0 = 1.0f - x20;
                float t20 = t0 * t0;
                float t40 = t20 * t20;
                float gx0 = grad1(i0, seed);
                float n0 = t40 * gx0 * x0;

                float x21 = x1 * x1;
                float t1 = 1.0f - x21;
                float t21 = t1 * t1;
                float t41 = t21 * t21;
                float gx1 = grad1(i1, seed);
                float n1 = t41 * gx1 * x1;

                // Sum up and scale the result.  The scale is empirical, to make it cover [-1,1], and to make it approximately match the range of our
                // Perlin noise implementation.
                const float scale = 0.36f;

                if (dnoise_dx != null)
                {
                    *dnoise_dx = t20 * t0 * gx0 * x20;
                    *dnoise_dx += t21 * t1 * gx1 * x21;
                    *dnoise_dx *= -8.0f;
                    *dnoise_dx += t40 * gx0 + t41 * gx1;
                    *dnoise_dx *= scale;
                }

                return scale * (n0 + n1);
            }

            // 2D simplex noise with derivatives.
            // If the last two arguments are not null, the analytic derivative (the 2D gradient of the scalar noise field) is also calculated.
            public static float simplexnoise2(float x, float y, int seed = 0, float* dnoise_dx = null, float* dnoise_dy = null)
            {
                // Skewing factors for 2D simplex grid:
                const float F2 = (float)0.366025403; // = 0.5*(sqrt(3.0)-1.0)
                const float G2 = (float)0.211324865; // = (3.0-Math.sqrt(3.0))/6.0
                float g0x = 0.0f, g0y = 0.0f, g1x = 0.0f, g1y = 0.0f, g2x = 0.0f, g2y = 0.0f;

                // Skew the input space to determine which simplex cell we're in
                float s = (x + y) * F2; // Hairy factor for 2D
                float xs = x + s;
                float ys = y + s;
                int i = fmath.ifloor(xs);
                int j = fmath.ifloor(ys);

                float t = (float)(i + j) * G2;
                float X0 = i - t; // Unskew the cell origin back to (x,y) space
                float Y0 = j - t;
                float x0 = x - X0; // The x,y distances from the cell origin
                float y0 = y - Y0;

                // For the 2D case, the simplex shape is an equilateral triangle. Determine which simplex we are in.
                int i1, j1; // Offsets for second (middle) corner of simplex in (i,j) coords
                if (x0 > y0)
                {
                    i1 = 1;
                    j1 = 0; // lower triangle, XY order: (0,0)->(1,0)->(1,1)
                }
                else
                {
                    i1 = 0;
                    j1 = 1; // upper triangle, YX order: (0,0)->(0,1)->(1,1)
                }

                // A step of (1,0) in (i,j) means a step of (1-c,-c) in (x,y), and a step of (0,1) in (i,j) means a step of (-c,1-c) in (x,y), where
                // c = (3-sqrt(3))/6
                float x1 = x0 - i1 + G2; // Offsets for middle corner in (x,y) unskewed coords
                float y1 = y0 - j1 + G2;
                float x2 = x0 - 1.0f + 2.0f * G2; // Offsets for last corner in (x,y) unskewed coords
                float y2 = y0 - 1.0f + 2.0f * G2;

                // Calculate the contribution from the three corners
                float t20 = 0.0f, t40 = 0.0f;
                float t21 = 0.0f, t41 = 0.0f;
                float t22 = 0.0f, t42 = 0.0f;
                float n0 = 0.0f, n1 = 0.0f, n2 = 0.0f; // Noise contributions from the simplex corners

                float t0 = 0.5f - x0 * x0 - y0 * y0;
                if (t0 >= 0.0f)
                {
                    grad2(i, j, seed, out g0x, out g0y);
                    t20 = t0 * t0;
                    t40 = t20 * t20;
                    n0 = t40 * (g0x * x0 + g0y * y0);
                }

                float t1 = 0.5f - x1 * x1 - y1 * y1;
                if (t1 >= 0.0f)
                {
                    grad2(i + i1, j + j1, seed, out g1x, out g1y);
                    t21 = t1 * t1;
                    t41 = t21 * t21;
                    n1 = t41 * (g1x * x1 + g1y * y1);
                }

                float t2 = 0.5f - x2 * x2 - y2 * y2;
                if (t2 >= 0.0f)
                {
                    grad2(i + 1, j + 1, seed, out g2x, out g2y);
                    t22 = t2 * t2;
                    t42 = t22 * t22;
                    n2 = t42 * (g2x * x2 + g2y * y2);
                }

                // Sum up and scale the result.
                const float scale = 64.0f;
                float noise = scale * (n0 + n1 + n2);

                // Compute derivative, if requested by supplying non-null pointers for the last two arguments
                if (dnoise_dx != null)
                {
                    float temp0 = t20 * t0 * (g0x * x0 + g0y * y0);
                    *dnoise_dx = temp0 * x0;
                    *dnoise_dy = temp0 * y0;
                    float temp1 = t21 * t1 * (g1x * x1 + g1y * y1);
                    *dnoise_dx += temp1 * x1;
                    *dnoise_dy += temp1 * y1;
                    float temp2 = t22 * t2 * (g2x * x2 + g2y * y2);
                    *dnoise_dx += temp2 * x2;
                    *dnoise_dy += temp2 * y2;
                    *dnoise_dx *= -8.0f;
                    *dnoise_dy *= -8.0f;
                    *dnoise_dx += t40 * g0x + t41 * g1x + t42 * g2x;
                    *dnoise_dy += t40 * g0y + t41 * g1y + t42 * g2y;
                    *dnoise_dx *= scale; // Scale derivative to match the noise scaling
                    *dnoise_dy *= scale;
                }
                return noise;
            }

            // 3D simplex noise with derivatives.
            // If the last three arguments are not null, the analytic derivative (the 3D gradient of the scalar noise field) is also calculated.
            public static float simplexnoise3(float x, float y, float z, int seed = 0, float* dnoise_dx = null, float* dnoise_dy = null, float* dnoise_dz = null)
            {
                // Skewing factors for 3D simplex grid:
                const float F3 = (float)0.333333333; // = 1/3
                const float G3 = (float)0.166666667; // = 1/6

                // Gradients at simplex corners
                float g0x = 0.0f, g0y = 0.0f, g0z = 0.0f;
                float g1x = 0.0f, g1y = 0.0f, g1z = 0.0f;
                float g2x = 0.0f, g2y = 0.0f, g2z = 0.0f;
                float g3x = 0.0f, g3y = 0.0f, g3z = 0.0f;

                // Skew the input space to determine which simplex cell we're in
                float s = (x + y + z) * F3; // Very nice and simple skew factor for 3D
                float xs = x + s;
                float ys = y + s;
                float zs = z + s;
                int i = fmath.ifloor(xs);
                int j = fmath.ifloor(ys);
                int k = fmath.ifloor(zs);

                float t = (float)(i + j + k) * G3;
                float X0 = i - t; // Unskew the cell origin back to (x,y,z) space
                float Y0 = j - t;
                float Z0 = k - t;
                float x0 = x - X0; // The x,y,z distances from the cell origin
                float y0 = y - Y0;
                float z0 = z - Z0;

                // For the 3D case, the simplex shape is a slightly irregular tetrahedron. Determine which simplex we are in.
                int i1, j1, k1; // Offsets for second corner of simplex in (i,j,k) coords
                int i2, j2, k2; // Offsets for third corner of simplex in (i,j,k) coords

                if (x0 >= y0)
                {
                    if (y0 >= z0)
                    {
                        i1 = 1; j1 = 0; k1 = 0; i2 = 1; j2 = 1; k2 = 0; // X Y Z order
                    }
                    else if (x0 >= z0)
                    {
                        i1 = 1; j1 = 0; k1 = 0; i2 = 1; j2 = 0; k2 = 1; // X Z Y order
                    }
                    else
                    {
                        i1 = 0; j1 = 0; k1 = 1; i2 = 1; j2 = 0; k2 = 1; // Z X Y order
                    }
                }
                else
                {
                    // x0<y0
                    if (y0 < z0)
                    {
                        i1 = 0; j1 = 0; k1 = 1; i2 = 0; j2 = 1; k2 = 1; // Z Y X order
                    }
                    else if (x0 < z0)
                    {
                        i1 = 0; j1 = 1; k1 = 0; i2 = 0; j2 = 1; k2 = 1; // Y Z X order
                    }
                    else
                    {
                        i1 = 0; j1 = 1; k1 = 0; i2 = 1; j2 = 1; k2 = 0; // Y X Z order
                    }
                }

                // A step of (1,0,0) in (i,j,k) means a step of (1-c,-c,-c) in (x,y,z), a step of (0,1,0) in (i,j,k) means a step of (-c,1-c,-c) in (x,y,z),
                // and a step of (0,0,1) in (i,j,k) means a step of (-c,-c,1-c) in (x,y,z), where c = 1/6.
                float x1 = x0 - i1 + G3; // Offsets for second corner in (x,y,z) coords
                float y1 = y0 - j1 + G3;
                float z1 = z0 - k1 + G3;
                float x2 = x0 - i2 + 2.0f * G3; // Offsets for third corner in (x,y,z) coords
                float y2 = y0 - j2 + 2.0f * G3;
                float z2 = z0 - k2 + 2.0f * G3;
                float x3 = x0 - 1.0f + 3.0f * G3; // Offsets for last corner in (x,y,z) coords
                float y3 = y0 - 1.0f + 3.0f * G3;
                float z3 = z0 - 1.0f + 3.0f * G3;

                float t20 = 0.0f, t40 = 0.0f;
                float t21 = 0.0f, t41 = 0.0f;
                float t22 = 0.0f, t42 = 0.0f;
                float t23 = 0.0f, t43 = 0.0f;
                float n0 = 0.0f, n1 = 0.0f, n2 = 0.0f, n3 = 0.0f; // Noise contributions from the four simplex corners

                // Calculate the contribution from the four corners
                float t0 = 0.5f - x0 * x0 - y0 * y0 - z0 * z0;
                if (t0 >= 0.0f)
                {
                    grad3(i, j, k, seed, out g0x, out g0y, out g0z);
                    t20 = t0 * t0;
                    t40 = t20 * t20;
                    n0 = t40 * (g0x * x0 + g0y * y0 + g0z * z0);
                }

                float t1 = 0.5f - x1 * x1 - y1 * y1 - z1 * z1;
                if (t1 >= 0.0f)
                {
                    grad3(i + i1, j + j1, k + k1, seed, out g1x, out g1y, out g1z);
                    t21 = t1 * t1;
                    t41 = t21 * t21;
                    n1 = t41 * (g1x * x1 + g1y * y1 + g1z * z1);
                }

                float t2 = 0.5f - x2 * x2 - y2 * y2 - z2 * z2;
                if (t2 >= 0.0f)
                {
                    grad3(i + i2, j + j2, k + k2, seed, out g2x, out g2y, out g2z);
                    t22 = t2 * t2;
                    t42 = t22 * t22;
                    n2 = t42 * (g2x * x2 + g2y * y2 + g2z * z2);
                }

                float t3 = 0.5f - x3 * x3 - y3 * y3 - z3 * z3;
                if (t3 >= 0.0f)
                {
                    grad3(i + 1, j + 1, k + 1, seed, out g3x, out g3y, out g3z);
                    t23 = t3 * t3;
                    t43 = t23 * t23;
                    n3 = t43 * (g3x * x3 + g3y * y3 + g3z * z3);
                }

                // Sum up and scale the result.
                const float scale = 68.0f;
                float noise = scale * (n0 + n1 + n2 + n3);

                // Compute derivative, if requested by supplying non-null pointers for the last three arguments
                if (dnoise_dx != null)
                {
                    float temp0 = t20 * t0 * (g0x * x0 + g0y * y0 + g0z * z0);
                    *dnoise_dx = temp0 * x0;
                    *dnoise_dy = temp0 * y0;
                    *dnoise_dz = temp0 * z0;
                    float temp1 = t21 * t1 * (g1x * x1 + g1y * y1 + g1z * z1);
                    *dnoise_dx += temp1 * x1;
                    *dnoise_dy += temp1 * y1;
                    *dnoise_dz += temp1 * z1;
                    float temp2 = t22 * t2 * (g2x * x2 + g2y * y2 + g2z * z2);
                    *dnoise_dx += temp2 * x2;
                    *dnoise_dy += temp2 * y2;
                    *dnoise_dz += temp2 * z2;
                    float temp3 = t23 * t3 * (g3x * x3 + g3y * y3 + g3z * z3);
                    *dnoise_dx += temp3 * x3;
                    *dnoise_dy += temp3 * y3;
                    *dnoise_dz += temp3 * z3;
                    *dnoise_dx *= -8.0f;
                    *dnoise_dy *= -8.0f;
                    *dnoise_dz *= -8.0f;
                    *dnoise_dx += t40 * g0x + t41 * g1x + t42 * g2x + t43 * g3x;
                    *dnoise_dy += t40 * g0y + t41 * g1y + t42 * g2y + t43 * g3y;
                    *dnoise_dz += t40 * g0z + t41 * g1z + t42 * g2z + t43 * g3z;
                    *dnoise_dx *= scale; // Scale derivative to match the noise scaling
                    *dnoise_dy *= scale;
                    *dnoise_dz *= scale;
                }

                return noise;
            }

            // 4D simplex noise with derivatives.
            // If the last four arguments are not null, the analytic derivative (the 4D gradient of the scalar noise field) is also calculated.
            public static float simplexnoise4(float x, float y, float z, float w, int seed = 0, float* dnoise_dx = null, float* dnoise_dy = null,
                                              float* dnoise_dz = null, float* dnoise_dw = null)
            {
                // The skewing and unskewing factors are hairy again for the 4D case
                const float F4 = (float)0.309016994; // F4 = (Math.sqrt(5.0)-1.0)/4.0
                const float G4 = (float)0.138196601; // G4 = (5.0-Math.sqrt(5.0))/20.0

                // Gradients at simplex corners
                float g0x = 0.0f, g0y = 0.0f, g0z = 0.0f, g0w = 0.0f;
                float g1x = 0.0f, g1y = 0.0f, g1z = 0.0f, g1w = 0.0f;
                float g2x = 0.0f, g2y = 0.0f, g2z = 0.0f, g2w = 0.0f;
                float g3x = 0.0f, g3y = 0.0f, g3z = 0.0f, g3w = 0.0f;
                float g4x = 0.0f, g4y = 0.0f, g4z = 0.0f, g4w = 0.0f;

                // Noise contributions from the four simplex corners
                float n0 = 0.0f, n1 = 0.0f, n2 = 0.0f, n3 = 0.0f, n4 = 0.0f;
                float t20 = 0.0f, t21 = 0.0f, t22 = 0.0f, t23 = 0.0f, t24 = 0.0f;
                float t40 = 0.0f, t41 = 0.0f, t42 = 0.0f, t43 = 0.0f, t44 = 0.0f;

                // Skew the (x,y,z,w) space to determine which cell of 24 simplices we're in
                float s = (x + y + z + w) * F4; // Factor for 4D skewing
                float xs = x + s;
                float ys = y + s;
                float zs = z + s;
                float ws = w + s;
                int i = fmath.ifloor(xs);
                int j = fmath.ifloor(ys);
                int k = fmath.ifloor(zs);
                int l = fmath.ifloor(ws);

                float t = (i + j + k + l) * G4; // Factor for 4D unskewing
                float X0 = i - t; // Unskew the cell origin back to (x,y,z,w) space
                float Y0 = j - t;
                float Z0 = k - t;
                float W0 = l - t;

                float x0 = x - X0; // The x,y,z,w distances from the cell origin
                float y0 = y - Y0;
                float z0 = z - Z0;
                float w0 = w - W0;

                // To find out which of the 24 possible simplices we're in, we need to determine the magnitude ordering of x0, y0, z0 and w0.
                int c1 = (x0 > y0) ? 32 : 0;
                int c2 = (x0 > z0) ? 16 : 0;
                int c3 = (y0 > z0) ? 8 : 0;
                int c4 = (x0 > w0) ? 4 : 0;
                int c5 = (y0 > w0) ? 2 : 0;
                int c6 = (z0 > w0) ? 1 : 0;
                int c = c1 | c2 | c3 | c4 | c5 | c6; // '|' is mostly faster than '+'

                int i1, j1, k1, l1; // The integer offsets for the second simplex corner
                int i2, j2, k2, l2; // The integer offsets for the third simplex corner
                int i3, j3, k3, l3; // The integer offsets for the fourth simplex corner

                // simplex[c] is a 4-vector with the numbers 0, 1, 2 and 3 in some order.
                int sc = c * 4;
                // The number 3 in the "simplex" array is at the position of the largest coordinate.
                i1 = simplex[sc + 0] >= 3 ? 1 : 0;
                j1 = simplex[sc + 1] >= 3 ? 1 : 0;
                k1 = simplex[sc + 2] >= 3 ? 1 : 0;
                l1 = simplex[sc + 3] >= 3 ? 1 : 0;
                // The number 2 in the "simplex" array is at the second largest coordinate.
                i2 = simplex[sc + 0] >= 2 ? 1 : 0;
                j2 = simplex[sc + 1] >= 2 ? 1 : 0;
                k2 = simplex[sc + 2] >= 2 ? 1 : 0;
                l2 = simplex[sc + 3] >= 2 ? 1 : 0;
                // The number 1 in the "simplex" array is at the second smallest coordinate.
                i3 = simplex[sc + 0] >= 1 ? 1 : 0;
                j3 = simplex[sc + 1] >= 1 ? 1 : 0;
                k3 = simplex[sc + 2] >= 1 ? 1 : 0;
                l3 = simplex[sc + 3] >= 1 ? 1 : 0;
                // The fifth corner has all coordinate offsets = 1, so no need to look that up.

                float x1 = x0 - i1 + G4; // Offsets for second corner in (x,y,z,w) coords
                float y1 = y0 - j1 + G4;
                float z1 = z0 - k1 + G4;
                float w1 = w0 - l1 + G4;
                float x2 = x0 - i2 + 2.0f * G4; // Offsets for third corner in (x,y,z,w) coords
                float y2 = y0 - j2 + 2.0f * G4;
                float z2 = z0 - k2 + 2.0f * G4;
                float w2 = w0 - l2 + 2.0f * G4;
                float x3 = x0 - i3 + 3.0f * G4; // Offsets for fourth corner in (x,y,z,w) coords
                float y3 = y0 - j3 + 3.0f * G4;
                float z3 = z0 - k3 + 3.0f * G4;
                float w3 = w0 - l3 + 3.0f * G4;
                float x4 = x0 - 1.0f + 4.0f * G4; // Offsets for last corner in (x,y,z,w) coords
                float y4 = y0 - 1.0f + 4.0f * G4;
                float z4 = z0 - 1.0f + 4.0f * G4;
                float w4 = w0 - 1.0f + 4.0f * G4;

                // Calculate the contribution from the five corners
                float t0 = 0.5f - x0 * x0 - y0 * y0 - z0 * z0 - w0 * w0;
                if (t0 >= 0.0f)
                {
                    t20 = t0 * t0;
                    t40 = t20 * t20;
                    grad4(i, j, k, l, seed, out g0x, out g0y, out g0z, out g0w);
                    n0 = t40 * (g0x * x0 + g0y * y0 + g0z * z0 + g0w * w0);
                }

                float t1 = 0.5f - x1 * x1 - y1 * y1 - z1 * z1 - w1 * w1;
                if (t1 >= 0.0f)
                {
                    t21 = t1 * t1;
                    t41 = t21 * t21;
                    grad4(i + i1, j + j1, k + k1, l + l1, seed, out g1x, out g1y, out g1z, out g1w);
                    n1 = t41 * (g1x * x1 + g1y * y1 + g1z * z1 + g1w * w1);
                }

                float t2 = 0.5f - x2 * x2 - y2 * y2 - z2 * z2 - w2 * w2;
                if (t2 >= 0.0f)
                {
                    t22 = t2 * t2;
                    t42 = t22 * t22;
                    grad4(i + i2, j + j2, k + k2, l + l2, seed, out g2x, out g2y, out g2z, out g2w);
                    n2 = t42 * (g2x * x2 + g2y * y2 + g2z * z2 + g2w * w2);
                }

                float t3 = 0.5f - x3 * x3 - y3 * y3 - z3 * z3 - w3 * w3;
                if (t3 >= 0.0f)
                {
                    t23 = t3 * t3;
                    t43 = t23 * t23;
                    grad4(i + i3, j + j3, k + k3, l + l3, seed, out g3x, out g3y, out g3z, out g3w);
                    n3 = t43 * (g3x * x3 + g3y * y3 + g3z * z3 + g3w * w3);
                }

                float t4 = 0.5f - x4 * x4 - y4 * y4 - z4 * z4 - w4 * w4;
                if (t4 >= 0.0f)
                {
                    t24 = t4 * t4;
                    t44 = t24 * t24;
                    grad4(i + 1, j + 1, k + 1, l + 1, seed, out g4x, out g4y, out g4z, out g4w);
                    n4 = t44 * (g4x * x4 + g4y * y4 + g4z * z4 + g4w * w4);
                }

                // Sum up and scale the result.
                const float scale = 54.0f;
                float noise = scale * (n0 + n1 + n2 + n3 + n4);

                // Compute derivative, if requested by supplying non-null pointers for the last four arguments
                if (dnoise_dx != null)
                {
                    float temp0 = t20 * t0 * (g0x * x0 + g0y * y0 + g0z * z0 + g0w * w0);
                    *dnoise_dx = temp0 * x0;
                    *dnoise_dy = temp0 * y0;
                    *dnoise_dz = temp0 * z0;
                    *dnoise_dw = temp0 * w0;
                    float temp1 = t21 * t1 * (g1x * x1 + g1y * y1 + g1z * z1 + g1w * w1);
                    *dnoise_dx += temp1 * x1;
                    *dnoise_dy += temp1 * y1;
                    *dnoise_dz += temp1 * z1;
                    *dnoise_dw += temp1 * w1;
                    float temp2 = t22 * t2 * (g2x * x2 + g2y * y2 + g2z * z2 + g2w * w2);
                    *dnoise_dx += temp2 * x2;
                    *dnoise_dy += temp2 * y2;
                    *dnoise_dz += temp2 * z2;
                    *dnoise_dw += temp2 * w2;
                    float temp3 = t23 * t3 * (g3x * x3 + g3y * y3 + g3z * z3 + g3w * w3);
                    *dnoise_dx += temp3 * x3;
                    *dnoise_dy += temp3 * y3;
                    *dnoise_dz += temp3 * z3;
                    *dnoise_dw += temp3 * w3;
                    float temp4 = t24 * t4 * (g4x * x4 + g4y * y4 + g4z * z4 + g4w * w4);
                    *dnoise_dx += temp4 * x4;
                    *dnoise_dy += temp4 * y4;
                    *dnoise_dz += temp4 * z4;
                    *dnoise_dw += temp4 * w4;
                    *dnoise_dx *= -8.0f;
                    *dnoise_dy *= -8.0f;
                    *dnoise_dz *= -8.0f;
                    *dnoise_dw *= -8.0f;
                    *dnoise_dx += t40 * g0x + t41 * g1x + t42 * g2x + t43 * g3x + t44 * g4x;
                    *dnoise_dy += t40 * g0y + t41 * g1y + t42 * g2y + t43 * g3y + t44 * g4y;
                    *dnoise_dz += t40 * g0z + t41 * g1z + t42 * g2z + t43 * g3z + t44 * g4z;
                    *dnoise_dw += t40 * g0w + t41 * g1w + t42 * g2w + t43 * g3w + t44 * g4w;
                    // Scale derivative to match the noise scaling
                    *dnoise_dx *= scale;
                    *dnoise_dy *= scale;
                    *dnoise_dz *= scale;
                    *dnoise_dw *= scale;
                }

                return noise;
            }
        }
    }
}
