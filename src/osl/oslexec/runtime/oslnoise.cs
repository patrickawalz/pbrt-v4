// OSL port: src/include/OSL/oslnoise.h
//
// The noise function implementations behind the noise/snoise/cellnoise/hashnoise/simplex shadeops (opnoise.cpp wraps them in extern "C" entry points).
//
// Notes on the port:
//  * The C++ implements 2D, 3D and 4D Perlin noise with OIIO::simd (vfloat4/vint4) code: the lattice-corner hashes and gradients are computed four at a
//    time and then interpolated in a particular order (3D: z first, then x, then y; 4D: for float results w last, for Dual2 results w first).
//    The C++ also keeps a scalar "ORIGINAL" path (CGScalar policy) whose interpolation order differs and so gives results that differ in the last bits.
//    The shadeops use the default policy (SIMD), so this port reproduces the SIMD path's operation order lane by lane, in scalar code.
//  * The C++ templates the perlin() function on the hash policy (HashScalar, HashVector, and their Periodic forms). The four policy structs are collapsed
//    into PerlinHash (a flag plus the periods); the scalar-versus-vector distinction is the result type of perlin(): a float or Dual2f result uses the hash
//    itself, a Vec3 or Dual2v3 result uses its three 8-bit slices (sliceup).
//  * Vec3 results are computed as three float results that share the corner hashes, which is bit-identical to the C++ (every lerp, grad and scale is
//    componentwise).

using System;
using System.Runtime.CompilerServices;
using OIIO;

namespace OSL
{
    namespace pvt
    {
        public static unsafe class OslNoise
        {
            // convert a 32 bit integer into a floating point number in [0,1]
            private const float convertFactor = (float)(1.0 / (double)uint.MaxValue);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static float bits_to_01(uint bits)
            {
                // divide by 2^32-1
                return (float)bits * convertFactor;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static uint bitcast_to_uint(float x)
            {
                return fmath.bitcast_to_uint(x);
            }

            // hash an array of N 32 bit values into a pseudo-random value based on my favorite hash: http://burtleburtle.net/bob/c/lookup3.c
            // (the hand unrolled versions of inthash for 1-5 parameters)
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static uint inthash(uint k0)
            {
                uint start_val = 0xdeadbeefu + (1u << 2) + 13u;
                uint a = start_val + k0;
                uint c = bjhash.bjfinal(a, start_val, start_val);
                return c;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static uint inthash(uint k0, uint k1)
            {
                uint start_val = 0xdeadbeefu + (2u << 2) + 13u;
                uint a = start_val + k0;
                uint b = start_val + k1;
                uint c = bjhash.bjfinal(a, b, start_val);
                return c;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static uint inthash(uint k0, uint k1, uint k2)
            {
                uint start_val = 0xdeadbeefu + (3u << 2) + 13u;
                uint a = start_val + k0;
                uint b = start_val + k1;
                uint c = start_val + k2;
                c = bjhash.bjfinal(a, b, c);
                return c;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static uint inthash(uint k0, uint k1, uint k2, uint k3)
            {
                uint start_val = 0xdeadbeefu + (4u << 2) + 13u;
                uint a = start_val + k0;
                uint b = start_val + k1;
                uint c = start_val + k2;
                bjhash.bjmix(ref a, ref b, ref c);
                a += k3;
                c = bjhash.bjfinal(a, b, c);
                return c;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static uint inthash(uint k0, uint k1, uint k2, uint k3, uint k4)
            {
                uint start_val = 0xdeadbeefu + (5u << 2) + 13u;
                uint a = start_val + k0;
                uint b = start_val + k1;
                uint c = start_val + k2;
                bjhash.bjmix(ref a, ref b, ref c);
                b += k4;
                a += k3;
                c = bjhash.bjfinal(a, b, c);
                return c;
            }

            public static int inthashi(int x)
            {
                return (int)inthash((uint)x);
            }

            public static int inthashf(float x)
            {
                return (int)inthash(bitcast_to_uint(x));
            }

            public static int inthashf(float x, float y)
            {
                return (int)inthash(bitcast_to_uint(x), bitcast_to_uint(y));
            }

            public static int inthashf(float* x)
            {
                return (int)inthash(bitcast_to_uint(x[0]), bitcast_to_uint(x[1]), bitcast_to_uint(x[2]));
            }

            public static int inthashf(float* x, float y)
            {
                return (int)inthash(bitcast_to_uint(x[0]), bitcast_to_uint(x[1]), bitcast_to_uint(x[2]), bitcast_to_uint(y));
            }

            // always return a value inside [0,b) - even for negative numbers
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static int imod(int a, int b)
            {
                int remainder = a % b;
                if (remainder < 0)
                {
                    remainder += b;
                }
                return remainder;
            }

            // floorfrac return ifloor as well as the fractional remainder
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static float floorfrac(float x, out int i)
            {
                i = fmath.ifloor(x);
                return x - i;
            }

            // floorfrac with derivs
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static Dual2f floorfrac(Dual2f x, out int i)
            {
                float frac = floorfrac(x.x, out i);
                // slope of x is not affected by this operation
                return new Dual2f(frac, x.dx, x.dy);
            }

            // Perlin 'fade' function.
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static float fade(float t)
            {
                return t * t * t * (t * (t * 6.0f - 15.0f) + 10.0f);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static Dual2f fade(Dual2f t)
            {
                return t * t * t * (t * (t * new Dual2f(6.0f) - new Dual2f(15.0f)) + new Dual2f(10.0f));
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static float negate_if(float val, bool cond)
            {
                return cond ? -val : val;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static Dual2f negate_if(Dual2f val, bool cond)
            {
                return cond ? -val : val;
            }

            // 1,2,3 and 4 dimensional gradient functions - perform a dot product against a randomly chosen vector. Note that the gradient vector is not
            // normalized, but this only affects the overall "scale" of the result, so we simply account for the scale by multiplying in the corresponding
            // "perlin" function. These factors were experimentally calculated to be: 1D: 0.188, 2D: 0.507, 3D: 0.936, 4D: 0.870
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static float grad(int hash, float x)
            {
                int h = hash & 15;
                float g = 1 + (h & 7); // 1, 2, .., 8
                if ((h & 8) != 0)
                {
                    g = -g; // random sign
                }
                return g * x; // dot-product
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static Dual2f grad(int hash, Dual2f x)
            {
                int h = hash & 15;
                float g = 1 + (h & 7); // 1, 2, .., 8
                if ((h & 8) != 0)
                {
                    g = -g; // random sign
                }
                return g * x; // dot-product
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static float grad(int hash, float x, float y)
            {
                // 8 possible directions (+-1,+-2) and (+-2,+-1)
                int h = hash & 7;
                float u = h < 4 ? x : y;
                float v = 2.0f * (h < 4 ? y : x);
                // compute the dot product with (x,y).
                return negate_if(u, (h & 1) != 0) + negate_if(v, (h & 2) != 0);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static Dual2f grad(int hash, Dual2f x, Dual2f y)
            {
                int h = hash & 7;
                Dual2f u = h < 4 ? x : y;
                Dual2f v = 2.0f * (h < 4 ? y : x);
                return negate_if(u, (h & 1) != 0) + negate_if(v, (h & 2) != 0);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static float grad(int hash, float x, float y, float z)
            {
                // use vectors pointing to the edges of the cube
                int h = hash & 15;
                float u = h < 8 ? x : y;
                float v = h < 4 ? y : ((h == 12 || h == 14) ? x : z);
                return negate_if(u, (h & 1) != 0) + negate_if(v, (h & 2) != 0);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static Dual2f grad(int hash, Dual2f x, Dual2f y, Dual2f z)
            {
                int h = hash & 15;
                Dual2f u = h < 8 ? x : y;
                Dual2f v = h < 4 ? y : ((h == 12 || h == 14) ? x : z);
                return negate_if(u, (h & 1) != 0) + negate_if(v, (h & 2) != 0);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static float grad(int hash, float x, float y, float z, float w)
            {
                // use vectors pointing to the edges of the hypercube
                int h = hash & 31;
                float u = h < 24 ? x : y;
                float v = h < 16 ? y : z;
                float s = h < 8 ? z : w;
                return negate_if(u, (h & 1) != 0) + negate_if(v, (h & 2) != 0) + negate_if(s, (h & 4) != 0);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static Dual2f grad(int hash, Dual2f x, Dual2f y, Dual2f z, Dual2f w)
            {
                int h = hash & 31;
                Dual2f u = h < 24 ? x : y;
                Dual2f v = h < 16 ? y : z;
                Dual2f s = h < 8 ? z : w;
                return negate_if(u, (h & 1) != 0) + negate_if(v, (h & 2) != 0) + negate_if(s, (h & 4) != 0);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static float scale1(float result) { return 0.2500f * result; }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static float scale2(float result) { return 0.6616f * result; }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static float scale3(float result) { return 0.9820f * result; }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static float scale4(float result) { return 0.8344f * result; }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static Dual2f scale1(Dual2f result) { return 0.2500f * result; }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static Dual2f scale2(Dual2f result) { return 0.6616f * result; }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static Dual2f scale3(Dual2f result) { return 0.9820f * result; }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static Dual2f scale4(Dual2f result) { return 0.8344f * result; }

            // OIIO::lerp(v0, v1, x) = v0*(1-x) + v1*x, for floats and (with Q = Dual2<float>) for duals
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static float lerp(float v0, float v1, float x)
            {
                return fmath.lerp(v0, v1, x);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static Dual2f lerp(Dual2f v0, Dual2f v1, Dual2f x)
            {
                return v0 * (new Dual2f(1.0f) - x) + v1 * x;
            }

            // The pieces of the policy structs HashScalar / HashVector / HashScalarPeriodic / HashVectorPeriodic: the lattice hash of integer coordinates,
            // wrapped by the period when there is one.
            public struct PerlinHash
            {
                public bool periodic;
                public int m_px, m_py, m_pz, m_pw;

                // Non-periodic (HashScalar, HashVector)
                public static PerlinHash nonperiodic()
                {
                    PerlinHash h = new PerlinHash();
                    h.periodic = false;
                    return h;
                }

                private static int period(float p)
                {
                    int m = fmath.ifloor(p);
                    if (m < 1)
                    {
                        m = 1;
                    }
                    return m;
                }

                // HashScalarPeriodic / HashVectorPeriodic constructors
                public PerlinHash(float px)
                {
                    periodic = true;
                    m_px = period(px);
                    m_py = m_pz = m_pw = 1;
                }

                public PerlinHash(float px, float py)
                {
                    periodic = true;
                    m_px = period(px);
                    m_py = period(py);
                    m_pz = m_pw = 1;
                }

                public PerlinHash(float px, float py, float pz)
                {
                    periodic = true;
                    m_px = period(px);
                    m_py = period(py);
                    m_pz = period(pz);
                    m_pw = 1;
                }

                public PerlinHash(float px, float py, float pz, float pw)
                {
                    periodic = true;
                    m_px = period(px);
                    m_py = period(py);
                    m_pz = period(pz);
                    m_pw = period(pw);
                }

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public uint hash(int x)
                {
                    return periodic ? inthash((uint)imod(x, m_px)) : inthash((uint)x);
                }

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public uint hash(int x, int y)
                {
                    return periodic ? inthash((uint)imod(x, m_px), (uint)imod(y, m_py)) : inthash((uint)x, (uint)y);
                }

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public uint hash(int x, int y, int z)
                {
                    return periodic ? inthash((uint)imod(x, m_px), (uint)imod(y, m_py), (uint)imod(z, m_pz)) : inthash((uint)x, (uint)y, (uint)z);
                }

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public uint hash(int x, int y, int z, int w)
                {
                    return periodic ? inthash((uint)imod(x, m_px), (uint)imod(y, m_py), (uint)imod(z, m_pz), (uint)imod(w, m_pw))
                                    : inthash((uint)x, (uint)y, (uint)z, (uint)w);
                }
            }

            // The hash a gradient lookup uses: the whole hash for a scalar (comp < 0), or one of the three 8-bit slices of it (sliceup) for a vector
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static int slice(uint h, int comp)
            {
                return comp < 0 ? (int)h : (int)((h >> (8 * comp)) & 0xFF);
            }

            //
            // The shared evaluation of the 2D/3D/4D lattice, given the corner hashes h[] (index x + 2y + 4z + 8w).
            //

            // The bilinear interpolation of the 4 corner gradients: lerp(lerp(a,b,u), lerp(c,d,u), v)
            private static float perlin_eval2(uint* h, int comp, float fx, float fy, float u, float v)
            {
                float g0 = grad(slice(h[0], comp), fx, fy);
                float g1 = grad(slice(h[1], comp), fx - 1.0f, fy);
                float g2 = grad(slice(h[2], comp), fx, fy - 1.0f);
                float g3 = grad(slice(h[3], comp), fx - 1.0f, fy - 1.0f);
                float xx0 = lerp(g0, g1, u);
                float xx2 = lerp(g2, g3, u);
                return scale2(lerp(xx0, xx2, v));
            }

            // Interpolate along the z axis first, then x, then y
            private static float perlin_eval3(uint* h, int comp, float fx, float fy, float fz, float u, float v, float w)
            {
                float z0g0 = grad(slice(h[0], comp), fx, fy, fz);
                float z0g1 = grad(slice(h[1], comp), fx - 1.0f, fy, fz);
                float z0g2 = grad(slice(h[2], comp), fx, fy - 1.0f, fz);
                float z0g3 = grad(slice(h[3], comp), fx - 1.0f, fy - 1.0f, fz);
                float z1g0 = grad(slice(h[4], comp), fx, fy, fz - 1.0f);
                float z1g1 = grad(slice(h[5], comp), fx - 1.0f, fy, fz - 1.0f);
                float z1g2 = grad(slice(h[6], comp), fx, fy - 1.0f, fz - 1.0f);
                float z1g3 = grad(slice(h[7], comp), fx - 1.0f, fy - 1.0f, fz - 1.0f);
                float xy0 = lerp(z0g0, z1g0, w);
                float xy1 = lerp(z0g1, z1g1, w);
                float xy2 = lerp(z0g2, z1g2, w);
                float xy3 = lerp(z0g3, z1g3, w);
                float xx0 = lerp(xy0, xy1, u);
                float xx2 = lerp(xy2, xy3, u);
                return scale3(lerp(xx0, xx2, v));
            }

            private static float perlin_eval4(uint* h, int comp, float fx, float fy, float fz, float fw, float u, float v, float t, float s)
            {
                // The four sets of four corner gradients: (z0,w0), (z1,w0), (z0,w1), (z1,w1)
                float* g = stackalloc float[16];
                for (int set = 0; set < 4; ++set)
                {
                    float rz = (set & 1) != 0 ? fz - 1.0f : fz;
                    float rw = (set & 2) != 0 ? fw - 1.0f : fw;
                    g[set * 4 + 0] = grad(slice(h[(set & 1) * 4 + (set >> 1) * 8 + 0], comp), fx, fy, rz, rw);
                    g[set * 4 + 1] = grad(slice(h[(set & 1) * 4 + (set >> 1) * 8 + 1], comp), fx - 1.0f, fy, rz, rw);
                    g[set * 4 + 2] = grad(slice(h[(set & 1) * 4 + (set >> 1) * 8 + 2], comp), fx, fy - 1.0f, rz, rw);
                    g[set * 4 + 3] = grad(slice(h[(set & 1) * 4 + (set >> 1) * 8 + 3], comp), fx - 1.0f, fy - 1.0f, rz, rw);
                }
                // trilerp(corner_grad_z0, corner_grad_z1, uvts) and trilerp(corner_grad_z2, corner_grad_z3, uvts)
                float tri0;
                {
                    float xy0 = lerp(g[0], g[4], t);
                    float xy1 = lerp(g[1], g[5], t);
                    float xy2 = lerp(g[2], g[6], t);
                    float xy3 = lerp(g[3], g[7], t);
                    float xx0 = lerp(xy0, xy1, u);
                    float xx2 = lerp(xy2, xy3, u);
                    tri0 = lerp(xx0, xx2, v);
                }
                float tri1;
                {
                    float xy0 = lerp(g[8], g[12], t);
                    float xy1 = lerp(g[9], g[13], t);
                    float xy2 = lerp(g[10], g[14], t);
                    float xy3 = lerp(g[11], g[15], t);
                    float xx0 = lerp(xy0, xy1, u);
                    float xx2 = lerp(xy2, xy3, u);
                    tri1 = lerp(xx0, xx2, v);
                }
                return scale4(lerp(tri0, tri1, s));
            }

            private static Dual2f perlin_eval2(uint* h, int comp, Dual2f fx, Dual2f fy, Dual2f u, Dual2f v)
            {
                Dual2f g0 = grad(slice(h[0], comp), fx, fy);
                Dual2f g1 = grad(slice(h[1], comp), fx - 1.0f, fy);
                Dual2f g2 = grad(slice(h[2], comp), fx, fy - 1.0f);
                Dual2f g3 = grad(slice(h[3], comp), fx - 1.0f, fy - 1.0f);
                Dual2f xx0 = lerp(g0, g1, u);
                Dual2f xx2 = lerp(g2, g3, u);
                return scale2(lerp(xx0, xx2, v));
            }

            private static Dual2f perlin_eval3(uint* h, int comp, Dual2f fx, Dual2f fy, Dual2f fz, Dual2f u, Dual2f v, Dual2f w)
            {
                Dual2f z0g0 = grad(slice(h[0], comp), fx, fy, fz);
                Dual2f z0g1 = grad(slice(h[1], comp), fx - 1.0f, fy, fz);
                Dual2f z0g2 = grad(slice(h[2], comp), fx, fy - 1.0f, fz);
                Dual2f z0g3 = grad(slice(h[3], comp), fx - 1.0f, fy - 1.0f, fz);
                Dual2f z1g0 = grad(slice(h[4], comp), fx, fy, fz - 1.0f);
                Dual2f z1g1 = grad(slice(h[5], comp), fx - 1.0f, fy, fz - 1.0f);
                Dual2f z1g2 = grad(slice(h[6], comp), fx, fy - 1.0f, fz - 1.0f);
                Dual2f z1g3 = grad(slice(h[7], comp), fx - 1.0f, fy - 1.0f, fz - 1.0f);
                Dual2f xy0 = lerp(z0g0, z1g0, w);
                Dual2f xy1 = lerp(z0g1, z1g1, w);
                Dual2f xy2 = lerp(z0g2, z1g2, w);
                Dual2f xy3 = lerp(z0g3, z1g3, w);
                Dual2f xx0 = lerp(xy0, xy1, u);
                Dual2f xx2 = lerp(xy2, xy3, u);
                return scale3(lerp(xx0, xx2, v));
            }

            // For Dual2 results the 4D interpolation goes along w first, then z, x and y (the float path does z, x, y and then w)
            private static Dual2f perlin_eval4(uint* h, int comp, Dual2f fx, Dual2f fy, Dual2f fz, Dual2f fw, Dual2f u, Dual2f v, Dual2f t, Dual2f s)
            {
                Dual2f* g = stackalloc Dual2f[16];
                for (int set = 0; set < 4; ++set)
                {
                    Dual2f rz = (set & 1) != 0 ? fz - 1.0f : fz;
                    Dual2f rw = (set & 2) != 0 ? fw - 1.0f : fw;
                    int b = (set & 1) * 4 + (set >> 1) * 8;
                    g[set * 4 + 0] = grad(slice(h[b + 0], comp), fx, fy, rz, rw);
                    g[set * 4 + 1] = grad(slice(h[b + 1], comp), fx - 1.0f, fy, rz, rw);
                    g[set * 4 + 2] = grad(slice(h[b + 2], comp), fx, fy - 1.0f, rz, rw);
                    g[set * 4 + 3] = grad(slice(h[b + 3], comp), fx - 1.0f, fy - 1.0f, rz, rw);
                }
                // corner_grad_z0 = set 0, z1 = set 1, z2 = set 2, z3 = set 3
                Dual2f xx0;
                Dual2f xx2;
                {
                    Dual2f xyz0_0 = lerp(g[0], g[8], s);
                    Dual2f xyz0_1 = lerp(g[1], g[9], s);
                    Dual2f xyz0_2 = lerp(g[2], g[10], s);
                    Dual2f xyz0_3 = lerp(g[3], g[11], s);
                    Dual2f xyz1_0 = lerp(g[4], g[12], s);
                    Dual2f xyz1_1 = lerp(g[5], g[13], s);
                    Dual2f xyz1_2 = lerp(g[6], g[14], s);
                    Dual2f xyz1_3 = lerp(g[7], g[15], s);
                    Dual2f xy0 = lerp(xyz0_0, xyz1_0, t);
                    Dual2f xy1 = lerp(xyz0_1, xyz1_1, t);
                    Dual2f xy2 = lerp(xyz0_2, xyz1_2, t);
                    Dual2f xy3 = lerp(xyz0_3, xyz1_3, t);
                    xx0 = lerp(xy0, xy1, u);
                    xx2 = lerp(xy2, xy3, u);
                }
                return scale4(lerp(xx0, xx2, v));
            }

            // The corner hashes, indexed x + 2y + 4z + 8w
            private static void fill_hash(PerlinHash ph, int X, uint* h)
            {
                h[0] = ph.hash(X);
                h[1] = ph.hash(X + 1);
            }

            private static void fill_hash(PerlinHash ph, int X, int Y, uint* h)
            {
                h[0] = ph.hash(X, Y);
                h[1] = ph.hash(X + 1, Y);
                h[2] = ph.hash(X, Y + 1);
                h[3] = ph.hash(X + 1, Y + 1);
            }

            private static void fill_hash(PerlinHash ph, int X, int Y, int Z, uint* h)
            {
                for (int k = 0; k < 2; ++k)
                {
                    h[k * 4 + 0] = ph.hash(X, Y, Z + k);
                    h[k * 4 + 1] = ph.hash(X + 1, Y, Z + k);
                    h[k * 4 + 2] = ph.hash(X, Y + 1, Z + k);
                    h[k * 4 + 3] = ph.hash(X + 1, Y + 1, Z + k);
                }
            }

            private static void fill_hash(PerlinHash ph, int X, int Y, int Z, int W, uint* h)
            {
                for (int l = 0; l < 2; ++l)
                {
                    for (int k = 0; k < 2; ++k)
                    {
                        int b = k * 4 + l * 8;
                        h[b + 0] = ph.hash(X, Y, Z + k, W + l);
                        h[b + 1] = ph.hash(X + 1, Y, Z + k, W + l);
                        h[b + 2] = ph.hash(X, Y + 1, Z + k, W + l);
                        h[b + 3] = ph.hash(X + 1, Y + 1, Z + k, W + l);
                    }
                }
            }

            // 1D: no SIMD path in the C++
            private static float perlin1(uint* h, int comp, float fx, float u)
            {
                return scale1(lerp(grad(slice(h[0], comp), fx), grad(slice(h[1], comp), fx - 1.0f), u));
            }

            private static Dual2f perlin1(uint* h, int comp, Dual2f fx, Dual2f u)
            {
                return scale1(lerp(grad(slice(h[0], comp), fx), grad(slice(h[1], comp), fx - 1.0f), u));
            }

            //
            // Perlin noise with a float result (the policy is HashScalar[Periodic]): result in about [-1,1]
            //
            public static float perlin(PerlinHash ph, float x)
            {
                uint* h = stackalloc uint[2];
                int X;
                float fx = floorfrac(x, out X);
                fill_hash(ph, X, h);
                return perlin1(h, -1, fx, fade(fx));
            }

            public static float perlin(PerlinHash ph, float x, float y)
            {
                uint* h = stackalloc uint[4];
                int X;
                int Y;
                float fx = floorfrac(x, out X);
                float fy = floorfrac(y, out Y);
                fill_hash(ph, X, Y, h);
                return perlin_eval2(h, -1, fx, fy, fade(fx), fade(fy));
            }

            public static float perlin(PerlinHash ph, float x, float y, float z)
            {
                uint* h = stackalloc uint[8];
                int X;
                int Y;
                int Z;
                float fx = floorfrac(x, out X);
                float fy = floorfrac(y, out Y);
                float fz = floorfrac(z, out Z);
                fill_hash(ph, X, Y, Z, h);
                return perlin_eval3(h, -1, fx, fy, fz, fade(fx), fade(fy), fade(fz));
            }

            public static float perlin(PerlinHash ph, float x, float y, float z, float w)
            {
                uint* h = stackalloc uint[16];
                int X;
                int Y;
                int Z;
                int W;
                float fx = floorfrac(x, out X);
                float fy = floorfrac(y, out Y);
                float fz = floorfrac(z, out Z);
                float fw = floorfrac(w, out W);
                fill_hash(ph, X, Y, Z, W, h);
                return perlin_eval4(h, -1, fx, fy, fz, fw, fade(fx), fade(fy), fade(fz), fade(fw));
            }

            //
            // Perlin noise with a Vec3 result (HashVector[Periodic]): r points at three floats
            //
            public static void perlinv(PerlinHash ph, float* r, float x)
            {
                uint* h = stackalloc uint[2];
                int X;
                float fx = floorfrac(x, out X);
                float u = fade(fx);
                fill_hash(ph, X, h);
                for (int c = 0; c < 3; ++c)
                {
                    r[c] = perlin1(h, c, fx, u);
                }
            }

            public static void perlinv(PerlinHash ph, float* r, float x, float y)
            {
                uint* h = stackalloc uint[4];
                int X;
                int Y;
                float fx = floorfrac(x, out X);
                float fy = floorfrac(y, out Y);
                float u = fade(fx);
                float v = fade(fy);
                fill_hash(ph, X, Y, h);
                for (int c = 0; c < 3; ++c)
                {
                    r[c] = perlin_eval2(h, c, fx, fy, u, v);
                }
            }

            public static void perlinv(PerlinHash ph, float* r, float x, float y, float z)
            {
                uint* h = stackalloc uint[8];
                int X;
                int Y;
                int Z;
                float fx = floorfrac(x, out X);
                float fy = floorfrac(y, out Y);
                float fz = floorfrac(z, out Z);
                float u = fade(fx);
                float v = fade(fy);
                float w = fade(fz);
                fill_hash(ph, X, Y, Z, h);
                for (int c = 0; c < 3; ++c)
                {
                    r[c] = perlin_eval3(h, c, fx, fy, fz, u, v, w);
                }
            }

            public static void perlinv(PerlinHash ph, float* r, float x, float y, float z, float w)
            {
                uint* h = stackalloc uint[16];
                int X;
                int Y;
                int Z;
                int W;
                float fx = floorfrac(x, out X);
                float fy = floorfrac(y, out Y);
                float fz = floorfrac(z, out Z);
                float fw = floorfrac(w, out W);
                float u = fade(fx);
                float v = fade(fy);
                float t = fade(fz);
                float s = fade(fw);
                fill_hash(ph, X, Y, Z, W, h);
                for (int c = 0; c < 3; ++c)
                {
                    r[c] = perlin_eval4(h, c, fx, fy, fz, fw, u, v, t, s);
                }
            }

            //
            // Dual2 versions
            //
            public static Dual2f perlin(PerlinHash ph, Dual2f x)
            {
                uint* h = stackalloc uint[2];
                int X;
                Dual2f fx = floorfrac(x, out X);
                fill_hash(ph, X, h);
                return perlin1(h, -1, fx, fade(fx));
            }

            public static Dual2f perlin(PerlinHash ph, Dual2f x, Dual2f y)
            {
                uint* h = stackalloc uint[4];
                int X;
                int Y;
                Dual2f fx = floorfrac(x, out X);
                Dual2f fy = floorfrac(y, out Y);
                fill_hash(ph, X, Y, h);
                return perlin_eval2(h, -1, fx, fy, fade(fx), fade(fy));
            }

            public static Dual2f perlin(PerlinHash ph, Dual2f x, Dual2f y, Dual2f z)
            {
                uint* h = stackalloc uint[8];
                int X;
                int Y;
                int Z;
                Dual2f fx = floorfrac(x, out X);
                Dual2f fy = floorfrac(y, out Y);
                Dual2f fz = floorfrac(z, out Z);
                fill_hash(ph, X, Y, Z, h);
                return perlin_eval3(h, -1, fx, fy, fz, fade(fx), fade(fy), fade(fz));
            }

            public static Dual2f perlin(PerlinHash ph, Dual2f x, Dual2f y, Dual2f z, Dual2f w)
            {
                uint* h = stackalloc uint[16];
                int X;
                int Y;
                int Z;
                int W;
                Dual2f fx = floorfrac(x, out X);
                Dual2f fy = floorfrac(y, out Y);
                Dual2f fz = floorfrac(z, out Z);
                Dual2f fw = floorfrac(w, out W);
                fill_hash(ph, X, Y, Z, W, h);
                return perlin_eval4(h, -1, fx, fy, fz, fw, fade(fx), fade(fy), fade(fz), fade(fw));
            }

            // Dual2<Vec3> results: r points at 9 floats {val xyz, dx xyz, dy xyz}
            private static void store(float* r, int c, Dual2f d)
            {
                r[c] = d.x;
                r[3 + c] = d.dx;
                r[6 + c] = d.dy;
            }

            public static void perlinv(PerlinHash ph, float* r, Dual2f x)
            {
                uint* h = stackalloc uint[2];
                int X;
                Dual2f fx = floorfrac(x, out X);
                Dual2f u = fade(fx);
                fill_hash(ph, X, h);
                for (int c = 0; c < 3; ++c)
                {
                    store(r, c, perlin1(h, c, fx, u));
                }
            }

            public static void perlinv(PerlinHash ph, float* r, Dual2f x, Dual2f y)
            {
                uint* h = stackalloc uint[4];
                int X;
                int Y;
                Dual2f fx = floorfrac(x, out X);
                Dual2f fy = floorfrac(y, out Y);
                Dual2f u = fade(fx);
                Dual2f v = fade(fy);
                fill_hash(ph, X, Y, h);
                for (int c = 0; c < 3; ++c)
                {
                    store(r, c, perlin_eval2(h, c, fx, fy, u, v));
                }
            }

            public static void perlinv(PerlinHash ph, float* r, Dual2f x, Dual2f y, Dual2f z)
            {
                uint* h = stackalloc uint[8];
                int X;
                int Y;
                int Z;
                Dual2f fx = floorfrac(x, out X);
                Dual2f fy = floorfrac(y, out Y);
                Dual2f fz = floorfrac(z, out Z);
                Dual2f u = fade(fx);
                Dual2f v = fade(fy);
                Dual2f w = fade(fz);
                fill_hash(ph, X, Y, Z, h);
                for (int c = 0; c < 3; ++c)
                {
                    store(r, c, perlin_eval3(h, c, fx, fy, fz, u, v, w));
                }
            }

            public static void perlinv(PerlinHash ph, float* r, Dual2f x, Dual2f y, Dual2f z, Dual2f w)
            {
                uint* h = stackalloc uint[16];
                int X;
                int Y;
                int Z;
                int W;
                Dual2f fx = floorfrac(x, out X);
                Dual2f fy = floorfrac(y, out Y);
                Dual2f fz = floorfrac(z, out Z);
                Dual2f fw = floorfrac(w, out W);
                Dual2f u = fade(fx);
                Dual2f v = fade(fy);
                Dual2f t = fade(fz);
                Dual2f s = fade(fw);
                fill_hash(ph, X, Y, Z, W, h);
                for (int c = 0; c < 3; ++c)
                {
                    store(r, c, perlin_eval4(h, c, fx, fy, fz, fw, u, v, t, s));
                }
            }

            //
            // Cell and hash noise
            //
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static uint cell_to_uint(float val)
            {
                return (uint)fmath.ifloor(val);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static uint hash_to_uint(float val)
            {
                return fmath.bitcast_to_uint(val);
            }

            // wrap for the periodic cell and hash noise
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static float wrap(float s, float period)
            {
                period = MathF.Floor(period);
                if (period < 1.0f)
                {
                    period = 1.0f;
                }
                return s - period * MathF.Floor(s / period);
            }

            // Scalar results of cellnoise (hash == false) and hashnoise (hash == true)
            public static float int_hash_noise(bool hash, float x)
            {
                return bits_to_01(inthash(hash ? hash_to_uint(x) : cell_to_uint(x)));
            }

            public static float int_hash_noise(bool hash, float x, float y)
            {
                if (hash)
                {
                    return bits_to_01(inthash(hash_to_uint(x), hash_to_uint(y)));
                }
                return bits_to_01(inthash(cell_to_uint(x), cell_to_uint(y)));
            }

            public static float int_hash_noise(bool hash, float x, float y, float z)
            {
                if (hash)
                {
                    return bits_to_01(inthash(hash_to_uint(x), hash_to_uint(y), hash_to_uint(z)));
                }
                return bits_to_01(inthash(cell_to_uint(x), cell_to_uint(y), cell_to_uint(z)));
            }

            public static float int_hash_noise(bool hash, float x, float y, float z, float w)
            {
                if (hash)
                {
                    return bits_to_01(inthash(hash_to_uint(x), hash_to_uint(y), hash_to_uint(z), hash_to_uint(w)));
                }
                return bits_to_01(inthash(cell_to_uint(x), cell_to_uint(y), cell_to_uint(z), cell_to_uint(w)));
            }

            // Vec3 results: the same hash with an extra seed 0, 1, 2 as the last argument
            public static void int_hash_noisev(bool hash, float* r, float x)
            {
                uint k0 = hash ? hash_to_uint(x) : cell_to_uint(x);
                for (uint c = 0; c < 3; ++c)
                {
                    r[c] = bits_to_01(inthash(k0, c));
                }
            }

            public static void int_hash_noisev(bool hash, float* r, float x, float y)
            {
                uint k0 = hash ? hash_to_uint(x) : cell_to_uint(x);
                uint k1 = hash ? hash_to_uint(y) : cell_to_uint(y);
                for (uint c = 0; c < 3; ++c)
                {
                    r[c] = bits_to_01(inthash(k0, k1, c));
                }
            }

            public static void int_hash_noisev(bool hash, float* r, float x, float y, float z)
            {
                uint k0 = hash ? hash_to_uint(x) : cell_to_uint(x);
                uint k1 = hash ? hash_to_uint(y) : cell_to_uint(y);
                uint k2 = hash ? hash_to_uint(z) : cell_to_uint(z);
                for (uint c = 0; c < 3; ++c)
                {
                    r[c] = bits_to_01(inthash(k0, k1, k2, c));
                }
            }

            public static void int_hash_noisev(bool hash, float* r, float x, float y, float z, float w)
            {
                uint k0 = hash ? hash_to_uint(x) : cell_to_uint(x);
                uint k1 = hash ? hash_to_uint(y) : cell_to_uint(y);
                uint k2 = hash ? hash_to_uint(z) : cell_to_uint(z);
                uint k3 = hash ? hash_to_uint(w) : cell_to_uint(w);
                for (uint c = 0; c < 3; ++c)
                {
                    r[c] = bits_to_01(inthash(k0, k1, k2, k3, c));
                }
            }
        }
    }
}
