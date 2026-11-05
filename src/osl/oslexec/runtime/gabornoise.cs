// OSL port: src/liboslnoise/gabornoise.cpp, src/liboslnoise/gabornoise.h
//
// Gabor noise (Lagae et al. 2009/2011), always evaluated with derivatives. The C++ code uses libm for exp, log, sqrt and cos (and OIIO::fast_exp2 and
// OIIO::fast_sincos when OSL_FAST_MATH is on); this port follows that, with MathF standing in for libm.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OIIO;

namespace OSL
{
    // Layout of the structure used to pass noise parameters (oslexec_pvt.h)
    [StructLayout(LayoutKind.Sequential)]
    public struct NoiseParams
    {
        public int anisotropic;
        public int do_filter;
        public Vec3 direction;
        public float bandwidth;
        public float impulses;

        public static NoiseParams create_default()
        {
            NoiseParams p = new NoiseParams();
            p.anisotropic = 0;
            p.do_filter = 1;
            p.direction = new Vec3(1.0f, 0.0f, 0.0f);
            p.bandwidth = 1.0f;
            p.impulses = 16.0f;
            return p;
        }
    }

    namespace pvt
    {
        // Imath::Matrix22<float> and Matrix33<float>, with only the operations gabor noise uses
        public struct Matrix22f
        {
            public float m00, m01, m10, m11;

            public Matrix22f(float a, float b, float c, float d)
            {
                m00 = a;
                m01 = b;
                m10 = c;
                m11 = d;
            }

            public static Matrix22f identity()
            {
                return new Matrix22f(1.0f, 0.0f, 0.0f, 1.0f);
            }

            public static Matrix22f operator +(Matrix22f a, Matrix22f b)
            {
                return new Matrix22f(a.m00 + b.m00, a.m01 + b.m01, a.m10 + b.m10, a.m11 + b.m11);
            }

            public static Matrix22f operator *(float a, Matrix22f v)
            {
                return new Matrix22f(v.m00 * a, v.m01 * a, v.m10 * a, v.m11 * a);
            }

            public static Matrix22f operator *(Matrix22f a, Matrix22f v)
            {
                // tmp[i][j] += x[i][k] * v[k][j], starting from zero
                float t00 = 0.0f;
                t00 += a.m00 * v.m00;
                t00 += a.m01 * v.m10;
                float t01 = 0.0f;
                t01 += a.m00 * v.m01;
                t01 += a.m01 * v.m11;
                float t10 = 0.0f;
                t10 += a.m10 * v.m00;
                t10 += a.m11 * v.m10;
                float t11 = 0.0f;
                t11 += a.m10 * v.m01;
                t11 += a.m11 * v.m11;
                return new Matrix22f(t00, t01, t10, t11);
            }

            public Matrix22f transposed()
            {
                return new Matrix22f(m00, m10, m01, m11);
            }

            public float determinant()
            {
                return m00 * m11 - m10 * m01;
            }

            // Imath::Matrix22<float>::inverse() (the noexcept form: a singular matrix gives the identity)
            public Matrix22f inverse()
            {
                float[] s = new float[4] { m11, -m01, -m10, m00 };
                float r = m00 * m11 - m10 * m01;
                if (MathF.Abs(r) >= 1.0f)
                {
                    for (int i = 0; i < 4; ++i)
                    {
                        s[i] /= r;
                    }
                }
                else
                {
                    float mr = MathF.Abs(r) / 1.17549435e-38f;
                    for (int i = 0; i < 4; ++i)
                    {
                        if (mr > MathF.Abs(s[i]))
                        {
                            s[i] /= r;
                        }
                        else
                        {
                            return identity();
                        }
                    }
                }
                return new Matrix22f(s[0], s[1], s[2], s[3]);
            }
        }

        public struct Matrix33f
        {
            public float m00, m01, m02, m10, m11, m12, m20, m21, m22;

            public Matrix33f(float a, float b, float c, float d, float e, float f, float g, float h, float i)
            {
                m00 = a;
                m01 = b;
                m02 = c;
                m10 = d;
                m11 = e;
                m12 = f;
                m20 = g;
                m21 = h;
                m22 = i;
            }

            // sfm::make_matrix33_cols
            public static Matrix33f make_matrix33_cols(Vec3 a, Vec3 b, Vec3 c)
            {
                return new Matrix33f(a.x, b.x, c.x, a.y, b.y, c.y, a.z, b.z, c.z);
            }

            public static Matrix33f operator *(Matrix33f a, Matrix33f v)
            {
                return new Matrix33f(
                    a.m00 * v.m00 + a.m01 * v.m10 + a.m02 * v.m20, a.m00 * v.m01 + a.m01 * v.m11 + a.m02 * v.m21, a.m00 * v.m02 + a.m01 * v.m12 + a.m02 * v.m22,
                    a.m10 * v.m00 + a.m11 * v.m10 + a.m12 * v.m20, a.m10 * v.m01 + a.m11 * v.m11 + a.m12 * v.m21, a.m10 * v.m02 + a.m11 * v.m12 + a.m12 * v.m22,
                    a.m20 * v.m00 + a.m21 * v.m10 + a.m22 * v.m20, a.m20 * v.m01 + a.m21 * v.m11 + a.m22 * v.m21, a.m20 * v.m02 + a.m21 * v.m12 + a.m22 * v.m22);
            }

            // Imath::Matrix33<float>::inverse() (the noexcept form: a singular matrix gives the identity)
            public Matrix33f inverse()
            {
                float[] x = new float[9] { m00, m01, m02, m10, m11, m12, m20, m21, m22 };
                float[] s;
                if (x[2] != 0.0f || x[5] != 0.0f || x[8] != 1.0f)
                {
                    s = new float[9]
                    {
                        x[4] * x[8] - x[7] * x[5], x[7] * x[2] - x[1] * x[8], x[1] * x[5] - x[4] * x[2],
                        x[6] * x[5] - x[3] * x[8], x[0] * x[8] - x[6] * x[2], x[3] * x[2] - x[0] * x[5],
                        x[3] * x[7] - x[6] * x[4], x[6] * x[1] - x[0] * x[7], x[0] * x[4] - x[3] * x[1],
                    };
                    float r = x[0] * s[0] + x[1] * s[3] + x[2] * s[6];
                    if (MathF.Abs(r) >= 1.0f)
                    {
                        for (int i = 0; i < 9; ++i)
                        {
                            s[i] /= r;
                        }
                    }
                    else
                    {
                        float mr = MathF.Abs(r) / 1.17549435e-38f;
                        for (int i = 0; i < 9; ++i)
                        {
                            if (mr > MathF.Abs(s[i]))
                            {
                                s[i] /= r;
                            }
                            else
                            {
                                return new Matrix33f(1.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0.0f, 0.0f, 0.0f, 1.0f);
                            }
                        }
                    }
                    return new Matrix33f(s[0], s[1], s[2], s[3], s[4], s[5], s[6], s[7], s[8]);
                }
                s = new float[9] { x[4], -x[1], 0.0f, -x[3], x[0], 0.0f, 0.0f, 0.0f, 1.0f };
                float r2 = x[0] * x[4] - x[3] * x[1];
                if (MathF.Abs(r2) >= 1.0f)
                {
                    for (int i = 0; i < 2; ++i)
                    {
                        for (int j = 0; j < 2; ++j)
                        {
                            s[i * 3 + j] /= r2;
                        }
                    }
                }
                else
                {
                    float mr = MathF.Abs(r2) / 1.17549435e-38f;
                    for (int i = 0; i < 2; ++i)
                    {
                        for (int j = 0; j < 2; ++j)
                        {
                            if (mr > MathF.Abs(s[i * 3 + j]))
                            {
                                s[i * 3 + j] /= r2;
                            }
                            else
                            {
                                return new Matrix33f(1.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0.0f, 0.0f, 0.0f, 1.0f);
                            }
                        }
                    }
                }
                s[6] = -x[6] * s[0] - x[7] * s[3];
                s[7] = -x[6] * s[1] - x[7] * s[4];
                return new Matrix33f(s[0], s[1], s[2], s[3], s[4], s[5], s[6], s[7], s[8]);
            }
        }

        public static unsafe class GaborNoise
        {
            private const float Gabor_Frequency = 2.0f;
            private const float Gabor_Impulse_Weight = 1.0f;
            private const float Gabor_Truncate = 0.02f;
            private const float M_TWO_PI_F = (float)(2.0 * Math.PI);
            private const float M_PI_F = (float)Math.PI;

            // Very fast random number generator based on [Borosh & Niederreiter 1983] linear congruential generator.
            private struct fast_rng
            {
                private uint m_seed;

                // seed based on the cell containing P
                public fast_rng(Vec3 p, int seed)
                {
                    m_seed = OslNoise.inthash((uint)fmath.ifloor(p.x), (uint)fmath.ifloor(p.y), (uint)fmath.ifloor(p.z), (uint)seed);
                    if (m_seed == 0)
                    {
                        m_seed = 1;
                    }
                }

                // Return uniform on [0,1)
                public float next()
                {
                    m_seed *= 3039177861u;
                    return (float)m_seed / (float)uint.MaxValue;
                }

                // Return poisson distribution with the given mean
                public int poisson(float mean)
                {
                    float g = MathF.Exp(-mean);
                    uint em = 0;
                    float t = next();
                    while (t > g)
                    {
                        ++em;
                        t *= next();
                    }
                    return (int)em;
                }
            }

            private struct GaborParams
            {
                public Vec3 omega;
                public int anisotropic;
                public bool do_filter;
                public float a;
                public float weight;
                public Vec3 N;
                public Matrix22f filter;
                public Matrix33f local;
                public float det_filter;
                public float bandwidth;
                public bool periodic;
                public Vec3 period;
                public float lambda;
                public float sqrt_lambda_inv;
                public float radius, radius2, radius3, radius_inv;
            }

            // Imath::clamp
            private static float clamp(float x, float lo, float hi)
            {
                return x > lo ? (x < hi ? x : hi) : lo;
            }

            private static GaborParams make_params(NoiseParams* opt)
            {
                GaborParams gp = new GaborParams();
                gp.omega = opt->direction;
                gp.anisotropic = opt->anisotropic;
                gp.do_filter = opt->do_filter != 0;
                gp.weight = Gabor_Impulse_Weight;
                gp.bandwidth = clamp(opt->bandwidth, 0.01f, 100.0f);
                gp.periodic = false;
                gp.det_filter = 0.0f;
                float TWO_to_bandwidth = fmath.fast_exp2(gp.bandwidth);
                // To match GCC result of sqrtf (M_PI / M_LN2)
                const float SQRT_PI_OVER_LN2 = 2.128934e+00f;
                // (TWO_to_bandwidth - 1.0) is a double expression in the C++
                gp.a = (float)((double)Gabor_Frequency * (((double)TWO_to_bandwidth - 1.0) / ((double)TWO_to_bandwidth + 1.0)) * (double)SQRT_PI_OVER_LN2);
                // Calculate the maximum radius from which we consider the kernel impulse centers -- derived from the threshold and bandwidth.
                gp.radius = MathF.Sqrt(-MathF.Log(Gabor_Truncate) / M_PI_F) / gp.a;
                gp.radius2 = gp.radius * gp.radius;
                gp.radius3 = gp.radius2 * gp.radius;
                gp.radius_inv = 1.0f / gp.radius;
                // Lambda is the impulse density.
                float impulses = clamp(opt->impulses, 1.0f, 32.0f);
                gp.lambda = impulses / ((float)(1.33333 * Math.PI) * gp.radius3);
                gp.sqrt_lambda_inv = 1.0f / MathF.Sqrt(gp.lambda);
                return gp;
            }

            // libm exp and cos of a Dual2 (OSL/dual.h, the non-fast flavours, which gabor uses)
            private static Dual2f exp(Dual2f a)
            {
                float f = MathF.Exp(a.x);
                return DualMath.dualfunc(a, f, f);
            }

            private static Dual2f cos(Dual2f a)
            {
                float sina = MathF.Sin(a.x);
                float cosa = MathF.Cos(a.x);
                return DualMath.dualfunc(a, cosa, -sina);
            }

            // Imath::Vec3<float>::normalize()
            private static Vec3 normalized(Vec3 v)
            {
                float l = OslRuntime.Vec3Length(v);
                if (l == 0.0f)
                {
                    return new Vec3(0.0f);
                }
                return new Vec3(v.x / l, v.y / l, v.z / l);
            }

            private static Vec3 normalize(Vec3 v)
            {
                float l = OslRuntime.Vec3Length(v);
                if (l != 0.0f)
                {
                    v.x /= l;
                    v.y /= l;
                    v.z /= l;
                }
                return v;
            }

            // dot(Vec3, Dual2<Vec3>) and dot(Dual2<Vec3>, Dual2<Vec3>) as in dual_vec.h
            private static Dual2f dot(Vec3 a, Dual2v3 b)
            {
                Dual2f bx = DualMath.comp_x(b);
                Dual2f by = DualMath.comp_y(b);
                Dual2f bz = DualMath.comp_z(b);
                return bx * a.x + by * a.y + bz * a.z;
            }

            // The Gabor kernel is a harmonic (cosine) modulated by a Gaussian envelope. This version is augmented with a phase, per [Lagae2011].
            private static Dual2f gabor_kernel(Dual2f weight, Vec3 omega, Dual2f phi, float bandwidth, Dual2v3 x)
            {
                // see Equation 1
                Dual2f g = exp(-M_PI_F * (bandwidth * bandwidth) * DualMath.dot(x, x));
                Dual2f h = cos(M_TWO_PI_F * dot(omega, x) + phi);
                return weight * g * h;
            }

            // The same kernel in 2D (Dual2<Vec2> x, Vec2 omega)
            private static Dual2f gabor_kernel_2d(Dual2f weight, float omega_x, float omega_y, Dual2f phi, float bandwidth, Dual2f xx, Dual2f xy)
            {
                Dual2f g = exp(-M_PI_F * (bandwidth * bandwidth) * (xx * xx + xy * xy));
                Dual2f h = cos(M_TWO_PI_F * (xx * omega_x + xy * omega_y) + phi);
                return weight * g * h;
            }

            private static void slice_gabor_kernel_3d(Dual2f d, float w, float a, Vec3 omega, float phi, out Dual2f w_s, out float omega_s_x, out float omega_s_y, out Dual2f phi_s)
            {
                // Equation 6
                w_s = w * exp(-M_PI_F * (a * a) * (d * d));
                omega_s_x = omega.x;
                omega_s_y = omega.y;
                phi_s = phi - M_TWO_PI_F * d * omega.z;
            }

            private static void gabor_mul_m22_v2(Matrix22f m, float vx, float vy, out float ox, out float oy)
            {
                ox = vx * m.m00 + vy * m.m10;
                oy = vx * m.m01 + vy * m.m11;
            }

            private static void filter_gabor_kernel_2d(Matrix22f filter, Dual2f w, float a, float omega_x, float omega_y, Dual2f phi,
                                                       out Dual2f w_f, out float a_f, out float omega_f_x, out float omega_f_y, out Dual2f phi_f)
            {
                // Equation 10
                Matrix22f Sigma_f = filter;
                Dual2f c_G = w;
                Matrix22f Sigma_G = (a * a / M_TWO_PI_F) * Matrix22f.identity();
                float c_F = 1.0f / (M_TWO_PI_F * MathF.Sqrt(Sigma_f.determinant()));
                Matrix22f Sigma_F = (float)(1.0 / (4.0 * Math.PI * Math.PI)) * Sigma_f.inverse();
                Matrix22f Sigma_G_Sigma_F = Sigma_G + Sigma_F;
                float gx;
                float gy;
                gabor_mul_m22_v2(Sigma_G_Sigma_F.inverse(), omega_x, omega_y, out gx, out gy);
                Dual2f c_GF = c_F * c_G * (1.0f / (M_TWO_PI_F * MathF.Sqrt(Sigma_G_Sigma_F.determinant()))) * MathF.Exp(-0.5f * (gx * omega_x + gy * omega_y));
                Matrix22f Sigma_G_i = Sigma_G.inverse();
                Matrix22f Sigma_GF = (Sigma_F.inverse() + Sigma_G_i).inverse();
                Matrix22f Sigma_GF_Gi = Sigma_GF * Sigma_G_i;
                gabor_mul_m22_v2(Sigma_GF_Gi, omega_x, omega_y, out omega_f_x, out omega_f_y);
                w_f = c_GF;
                a_f = MathF.Sqrt((float)((Math.PI * 2.0) * MathF.Sqrt(Sigma_GF.determinant()))); // M_TWO_PI is a double here
                phi_f = phi;
            }

            private static float wrap(float s, float period)
            {
                return OslNoise.wrap(s, period);
            }

            private static Vec3 wrap(Vec3 s, Vec3 period)
            {
                return new Vec3(wrap(s.x, period.x), wrap(s.y, period.y), wrap(s.z, period.z));
            }

            // Normalize v and set a and b to be unit vectors (any two unit vectors) that are orthogonal to v and each other.
            private static void make_orthonormals(ref Vec3 v, out Vec3 a, out Vec3 b)
            {
                v = normalize(v);
                if (MathF.Abs(v.x) < 0.9f)
                {
                    a = new Vec3(0.0f, v.z, -v.y); // v X (1,0,0)
                }
                else
                {
                    a = new Vec3(-v.z, 0.0f, v.x); // v X (0,1,0)
                }
                a = normalize(a);
                b = v.cross(a);
            }

            // Choose an omega and phi value for a particular gabor impulse, based on the user-selected noise mode.
            private static void gabor_sample(ref GaborParams gp, ref fast_rng rng, out Vec3 omega, out float phi)
            {
                // section 3.3, solid random-phase gabor noise
                if (gp.anisotropic == 1)
                {
                    omega = gp.omega;
                }
                else if (gp.anisotropic == 0)
                {
                    float omega_t = M_TWO_PI_F * rng.next();
                    float cos_omega_p = fmath.lerp(-1.0f, 1.0f, rng.next());
                    float sin_omega_p = MathF.Sqrt(MathF.Max(0.0f, 1.0f - cos_omega_p * cos_omega_p));
                    float sin_omega_t;
                    float cos_omega_t;
                    fmath.fast_sincos(omega_t, &sin_omega_t, &cos_omega_t);
                    omega = normalized(new Vec3(cos_omega_t * sin_omega_p, sin_omega_t * sin_omega_p, cos_omega_p));
                }
                else
                {
                    // otherwise hybrid
                    float omega_r = OslRuntime.Vec3Length(gp.omega);
                    float omega_t = M_TWO_PI_F * rng.next();
                    float sin_omega_t;
                    float cos_omega_t;
                    fmath.fast_sincos(omega_t, &sin_omega_t, &cos_omega_t);
                    omega = omega_r * new Vec3(cos_omega_t, sin_omega_t, 0.0f);
                }
                phi = M_TWO_PI_F * rng.next();
            }

            // Multiply a row 3-vector by a 3x3 matrix (Imathx multMatrix), plain and with derivatives (dual_vec.h)
            private static Vec3 multMatrix(Matrix33f M, Vec3 src)
            {
                float a = src.x * M.m00 + src.y * M.m10 + src.z * M.m20;
                float b = src.x * M.m01 + src.y * M.m11 + src.z * M.m21;
                float c = src.x * M.m02 + src.y * M.m12 + src.z * M.m22;
                return new Vec3(a, b, c);
            }

            private static Dual2v3 multMatrix(Matrix33f M, Dual2v3 src)
            {
                Dual2f src0 = DualMath.comp_x(src);
                Dual2f src1 = DualMath.comp_y(src);
                Dual2f src2 = DualMath.comp_z(src);
                Dual2f a = src0 * M.m00 + src1 * M.m10 + src2 * M.m20;
                Dual2f b = src0 * M.m01 + src1 * M.m11 + src2 * M.m21;
                Dual2f c = src0 * M.m02 + src1 * M.m12 + src2 * M.m22;
                return DualMath.make_Vec3(a, b, c);
            }

            // Evaluate the summed contribution of all gabor impulses within the cell whose corner is c_i. x_c_i is vector from x (the point we are trying
            // to evaluate noise at) and c_i.
            private static Dual2f gabor_cell(ref GaborParams gp, Vec3 c_i, Dual2v3 x_c_i, int seed)
            {
                fast_rng rng = new fast_rng(gp.periodic ? wrap(c_i, gp.period) : c_i, seed);
                int n_impulses = rng.poisson(gp.lambda * gp.radius3);
                Dual2f sum = new Dual2f(0.0f);

                for (int i = 0; i < n_impulses; i++)
                {
                    // the order of the rng() calls is z, y, x (see the comment in the C++)
                    float z_rng = rng.next();
                    float y_rng = rng.next();
                    float x_rng = rng.next();
                    Vec3 x_i_c = new Vec3(x_rng, y_rng, z_rng);
                    Dual2v3 x_k_i = gp.radius * (x_c_i - x_i_c);
                    float phi_i;
                    Vec3 omega_i;
                    gabor_sample(ref gp, ref rng, out omega_i, out phi_i);
                    if (x_k_i.x.length2() < gp.radius2)
                    {
                        if (!gp.do_filter)
                        {
                            sum += gabor_kernel(new Dual2f(gp.weight), omega_i, new Dual2f(phi_i), gp.a, x_k_i); // 3D
                        }
                        else
                        {
                            // Transform the impulse's anisotropy into tangent space
                            Vec3 omega_i_t = multMatrix(gp.local, omega_i);

                            // Slice to get a 2D kernel
                            Dual2f d_i = -dot(gp.N, x_k_i);
                            Dual2f w_i_t_s;
                            float omega_i_t_s_x;
                            float omega_i_t_s_y;
                            Dual2f phi_i_t_s;
                            slice_gabor_kernel_3d(d_i, gp.weight, gp.a, omega_i_t, phi_i, out w_i_t_s, out omega_i_t_s_x, out omega_i_t_s_y, out phi_i_t_s);

                            // Filter the 2D kernel
                            Dual2f w_i_t_s_f;
                            float a_i_t_s_f;
                            float omega_i_t_s_f_x;
                            float omega_i_t_s_f_y;
                            Dual2f phi_i_t_s_f;
                            filter_gabor_kernel_2d(gp.filter, w_i_t_s, gp.a, omega_i_t_s_x, omega_i_t_s_y, phi_i_t_s,
                                                   out w_i_t_s_f, out a_i_t_s_f, out omega_i_t_s_f_x, out omega_i_t_s_f_y, out phi_i_t_s_f);

                            // Now evaluate the 2D filtered kernel
                            Dual2v3 xkit = multMatrix(gp.local, x_k_i);
                            Dual2f gk = gabor_kernel_2d(w_i_t_s_f, omega_i_t_s_f_x, omega_i_t_s_f_y, phi_i_t_s_f, a_i_t_s_f, DualMath.comp_x(xkit), DualMath.comp_y(xkit));
                            if (!float.IsFinite(gk.x))
                            {
                                // Numeric failure of the filtered version. Fall back on the unfiltered.
                                gk = gabor_kernel(new Dual2f(gp.weight), omega_i, new Dual2f(phi_i), gp.a, x_k_i); // 3D
                            }
                            sum += gk;
                        }
                    }
                }

                return sum;
            }

            // Sum the contributions of gabor impulses in all neighboring cells surrounding position x_g.
            private static Dual2f gabor_grid(ref GaborParams gp, Dual2v3 x_g, int seed)
            {
                Vec3 floor_x_g = new Vec3(MathF.Floor(x_g.x.x), MathF.Floor(x_g.x.y), MathF.Floor(x_g.x.z)); // Vec3 because floor has no derivs
                Dual2v3 x_c = x_g - floor_x_g;
                Dual2f sum = new Dual2f(0.0f);

                for (int k = -1; k <= 1; k++)
                {
                    for (int j = -1; j <= 1; j++)
                    {
                        for (int i = -1; i <= 1; i++)
                        {
                            Vec3 c = new Vec3((float)i, (float)j, (float)k);
                            Vec3 c_i = floor_x_g + c;
                            Dual2v3 x_c_i = x_c - c;
                            sum += gabor_cell(ref gp, c_i, x_c_i, seed);
                        }
                    }
                }
                return sum * gp.sqrt_lambda_inv;
            }

            private static Dual2f gabor_evaluate(ref GaborParams gp, Dual2v3 x, int seed)
            {
                Dual2v3 x_g = gp.radius_inv * x;
                return gabor_grid(ref gp, x_g, seed);
            }

            // set up the filter matrix
            private static void gabor_setup_filter(Dual2v3 P, ref GaborParams gp)
            {
                // Make texture-space normal, tangent, bitangent
                Vec3 n = P.dx.cross(P.dy); // normal to P
                if (n.dot(n) < 1.0e-6f) // length of deriv < 1/1000
                {
                    // No way to do filter if we have no derivs, and no reason to do it if it's too small to have any effect.
                    gp.do_filter = false;
                    return; // we won't need anything else if filtering is off
                }
                Vec3 t;
                Vec3 b;
                make_orthonormals(ref n, out t, out b);

                // Rotations from tangent<->texture space
                Matrix33f Mtex_to_tan = Matrix33f.make_matrix33_cols(t, b, n); // M3_local
                Matrix33f Mscreen_to_tex = Matrix33f.make_matrix33_cols(P.dx, P.dy, new Vec3(0.0f, 0.0f, 0.0f));
                Matrix33f Mscreen_to_tan = Mscreen_to_tex * Mtex_to_tan; // M3_scr_tan
                Matrix22f M_scr_tan = new Matrix22f(Mscreen_to_tan.m00, Mscreen_to_tan.m01, Mscreen_to_tan.m10, Mscreen_to_tan.m11);
                float sigma_f_scr = 0.5f;
                Matrix22f Sigma_f_scr = new Matrix22f(sigma_f_scr * sigma_f_scr, 0.0f, 0.0f, sigma_f_scr * sigma_f_scr);
                Matrix22f M_scr_tan_t = M_scr_tan.transposed();
                Matrix22f Sigma_f_tan = M_scr_tan_t * Sigma_f_scr * M_scr_tan;

                gp.N = n;
                gp.filter = Sigma_f_tan;
                gp.det_filter = Sigma_f_tan.m00 * Sigma_f_tan.m11 - Sigma_f_tan.m01 * Sigma_f_tan.m10;
                gp.local = Mtex_to_tan;
                if (gp.det_filter < 1.0e-18f)
                {
                    gp.do_filter = false;
                    // Turn off filtering when tiny values will lead to numerical errors later if we filter.
                }
            }

            private static float result_scale(ref GaborParams gp)
            {
                float gabor_variance = 1.0f / (4.0f * MathF.Sqrt(2.0f) * (gp.a * gp.a * gp.a));
                float scale = 1.0f / (3.0f * MathF.Sqrt(gabor_variance));
                scale *= 0.5f; // empirical -- make it fit in [-1..1]
                return scale;
            }

            private static Dual2v3 make_Vec3(Dual2f x)
            {
                return new Dual2v3(new Vec3(x.x, 0.0f, 0.0f), new Vec3(x.dx, 0.0f, 0.0f), new Vec3(x.dy, 0.0f, 0.0f));
            }

            private static Dual2v3 make_Vec3(Dual2f x, Dual2f y)
            {
                return new Dual2v3(new Vec3(x.x, y.x, 0.0f), new Vec3(x.dx, y.dx, 0.0f), new Vec3(x.dy, y.dy, 0.0f));
            }

            //
            // Entry points: gabor (float result) and gabor3 (Vec3 result), plain and periodic
            //
            public static Dual2f gabor(Dual2f x, NoiseParams* opt)
            {
                // for now, just slice 3D
                return gabor(make_Vec3(x), opt);
            }

            public static Dual2f gabor(Dual2f x, Dual2f y, NoiseParams* opt)
            {
                return gabor(make_Vec3(x, y), opt);
            }

            public static Dual2f gabor(Dual2v3 P, NoiseParams* opt)
            {
                GaborParams gp = make_params(opt);
                if (gp.do_filter)
                {
                    gabor_setup_filter(P, ref gp);
                }
                Dual2f result = gabor_evaluate(ref gp, P, 0);
                return result * result_scale(ref gp);
            }

            public static Dual2v3 gabor3(Dual2f x, NoiseParams* opt)
            {
                return gabor3(make_Vec3(x), opt);
            }

            public static Dual2v3 gabor3(Dual2f x, Dual2f y, NoiseParams* opt)
            {
                return gabor3(make_Vec3(x, y), opt);
            }

            public static Dual2v3 gabor3(Dual2v3 P, NoiseParams* opt)
            {
                GaborParams gp = make_params(opt);
                if (gp.do_filter)
                {
                    gabor_setup_filter(P, ref gp);
                }
                Dual2f r0 = gabor_evaluate(ref gp, P, 0);
                Dual2f r1 = gabor_evaluate(ref gp, P, 1);
                Dual2f r2 = gabor_evaluate(ref gp, P, 2);
                float scale = result_scale(ref gp);
                return DualMath.make_Vec3(r0 * scale, r1 * scale, r2 * scale);
            }

            public static Dual2f pgabor(Dual2f x, float xperiod, NoiseParams* opt)
            {
                return pgabor(make_Vec3(x), new Vec3(xperiod, 0.0f, 0.0f), opt);
            }

            public static Dual2f pgabor(Dual2f x, Dual2f y, float xperiod, float yperiod, NoiseParams* opt)
            {
                return pgabor(make_Vec3(x, y), new Vec3(xperiod, yperiod, 0.0f), opt);
            }

            public static Dual2f pgabor(Dual2v3 P, Vec3 Pperiod, NoiseParams* opt)
            {
                GaborParams gp = make_params(opt);
                gp.periodic = true;
                gp.period = Pperiod;
                if (gp.do_filter)
                {
                    gabor_setup_filter(P, ref gp);
                }
                Dual2f result = gabor_evaluate(ref gp, P, 0);
                return result * result_scale(ref gp);
            }

            public static Dual2v3 pgabor3(Dual2f x, float xperiod, NoiseParams* opt)
            {
                return pgabor3(make_Vec3(x), new Vec3(xperiod, 0.0f, 0.0f), opt);
            }

            public static Dual2v3 pgabor3(Dual2f x, Dual2f y, float xperiod, float yperiod, NoiseParams* opt)
            {
                return pgabor3(make_Vec3(x, y), new Vec3(xperiod, yperiod, 0.0f), opt);
            }

            public static Dual2v3 pgabor3(Dual2v3 P, Vec3 Pperiod, NoiseParams* opt)
            {
                GaborParams gp = make_params(opt);
                gp.periodic = true;
                gp.period = Pperiod;
                if (gp.do_filter)
                {
                    gabor_setup_filter(P, ref gp);
                }
                Dual2f r0 = gabor_evaluate(ref gp, P, 0);
                Dual2f r1 = gabor_evaluate(ref gp, P, 1);
                Dual2f r2 = gabor_evaluate(ref gp, P, 2);
                float scale = result_scale(ref gp);
                return DualMath.make_Vec3(r0 * scale, r1 * scale, r2 * scale);
            }
        }
    }
}
