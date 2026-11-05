// OSL port: src/liboslexec/opmatrix.cpp
//
// The matrix shadeops that need no ShadingContext. The context dependent ones are not ported: osl_get_matrix, osl_get_inverse_matrix,
// osl_prepend_matrix_from, osl_get_from_to_matrix, osl_transform_triple and osl_transform_triple_nonlinear (they look up named coordinate systems through
// the renderer).
//
// Matrix44 arithmetic follows Imath 3.1 (Matrix44::multiply, inverse and gjInverse) and Imathx.h (det4x4, inlinedTransposed), operation for operation.

using System;
using System.Runtime.CompilerServices;
using OIIO;

namespace OSL
{
    public static unsafe partial class OslRuntime
    {
        // Imath::Matrix44<float>::multiply
        private static Matrix44 mat_multiply(Matrix44 a, Matrix44 b)
        {
            float a00 = a.m00;
            float a01 = a.m01;
            float a02 = a.m02;
            float a03 = a.m03;
            float c00 = a00 * b.m00 + a01 * b.m10 + a02 * b.m20 + a03 * b.m30;
            float c01 = a00 * b.m01 + a01 * b.m11 + a02 * b.m21 + a03 * b.m31;
            float c02 = a00 * b.m02 + a01 * b.m12 + a02 * b.m22 + a03 * b.m32;
            float c03 = a00 * b.m03 + a01 * b.m13 + a02 * b.m23 + a03 * b.m33;

            float a10 = a.m10;
            float a11 = a.m11;
            float a12 = a.m12;
            float a13 = a.m13;
            float c10 = a10 * b.m00 + a11 * b.m10 + a12 * b.m20 + a13 * b.m30;
            float c11 = a10 * b.m01 + a11 * b.m11 + a12 * b.m21 + a13 * b.m31;
            float c12 = a10 * b.m02 + a11 * b.m12 + a12 * b.m22 + a13 * b.m32;
            float c13 = a10 * b.m03 + a11 * b.m13 + a12 * b.m23 + a13 * b.m33;

            float a20 = a.m20;
            float a21 = a.m21;
            float a22 = a.m22;
            float a23 = a.m23;
            float c20 = a20 * b.m00 + a21 * b.m10 + a22 * b.m20 + a23 * b.m30;
            float c21 = a20 * b.m01 + a21 * b.m11 + a22 * b.m21 + a23 * b.m31;
            float c22 = a20 * b.m02 + a21 * b.m12 + a22 * b.m22 + a23 * b.m32;
            float c23 = a20 * b.m03 + a21 * b.m13 + a22 * b.m23 + a23 * b.m33;

            float a30 = a.m30;
            float a31 = a.m31;
            float a32 = a.m32;
            float a33 = a.m33;
            float c30 = a30 * b.m00 + a31 * b.m10 + a32 * b.m20 + a33 * b.m30;
            float c31 = a30 * b.m01 + a31 * b.m11 + a32 * b.m21 + a33 * b.m31;
            float c32 = a30 * b.m02 + a31 * b.m12 + a32 * b.m22 + a33 * b.m32;
            float c33 = a30 * b.m03 + a31 * b.m13 + a32 * b.m23 + a33 * b.m33;
            return new Matrix44(c00, c01, c02, c03, c10, c11, c12, c13, c20, c21, c22, c23, c30, c31, c32, c33);
        }

        // Matrix44 * float and float * Matrix44 (every element times the scalar)
        private static Matrix44 mat_scale(Matrix44 v, float a)
        {
            return new Matrix44(v.m00 * a, v.m01 * a, v.m02 * a, v.m03 * a,
                                v.m10 * a, v.m11 * a, v.m12 * a, v.m13 * a,
                                v.m20 * a, v.m21 * a, v.m22 * a, v.m23 * a,
                                v.m30 * a, v.m31 * a, v.m32 * a, v.m33 * a);
        }

        // Imathx inlinedTransposed
        private static Matrix44 mat_transposed(Matrix44 m)
        {
            return new Matrix44(m.m00, m.m10, m.m20, m.m30,
                                m.m01, m.m11, m.m21, m.m31,
                                m.m02, m.m12, m.m22, m.m32,
                                m.m03, m.m13, m.m23, m.m33);
        }

        // Imath::Matrix44<float>::gjInverse(): Gauss-Jordan elimination with partial pivoting; a singular matrix gives the identity
        private static Matrix44 mat_gjinverse(Matrix44 m)
        {
            float* t = stackalloc float[16];
            float* s = stackalloc float[16];
            for (int n = 0; n < 16; ++n)
            {
                t[n] = m.Get(n);
                s[n] = (n % 5 == 0) ? 1.0f : 0.0f;
            }

            // Forward elimination
            for (int i = 0; i < 3; i++)
            {
                int pivot = i;
                float pivotsize = t[i * 4 + i];
                if (pivotsize < 0)
                {
                    pivotsize = -pivotsize;
                }

                for (int j = i + 1; j < 4; j++)
                {
                    float tmp = t[j * 4 + i];
                    if (tmp < 0)
                    {
                        tmp = -tmp;
                    }
                    if (tmp > pivotsize)
                    {
                        pivot = j;
                        pivotsize = tmp;
                    }
                }

                if (pivotsize == 0)
                {
                    return Matrix44.identity;
                }

                if (pivot != i)
                {
                    for (int j = 0; j < 4; j++)
                    {
                        float tmp = t[i * 4 + j];
                        t[i * 4 + j] = t[pivot * 4 + j];
                        t[pivot * 4 + j] = tmp;

                        tmp = s[i * 4 + j];
                        s[i * 4 + j] = s[pivot * 4 + j];
                        s[pivot * 4 + j] = tmp;
                    }
                }

                for (int j = i + 1; j < 4; j++)
                {
                    float f = t[j * 4 + i] / t[i * 4 + i];
                    for (int k = 0; k < 4; k++)
                    {
                        t[j * 4 + k] -= f * t[i * 4 + k];
                        s[j * 4 + k] -= f * s[i * 4 + k];
                    }
                }
            }

            // Backward substitution
            for (int i = 3; i >= 0; --i)
            {
                float f = t[i * 4 + i];
                if (f == 0)
                {
                    return Matrix44.identity;
                }

                for (int j = 0; j < 4; j++)
                {
                    t[i * 4 + j] /= f;
                    s[i * 4 + j] /= f;
                }

                for (int j = 0; j < i; j++)
                {
                    f = t[j * 4 + i];
                    for (int k = 0; k < 4; k++)
                    {
                        t[j * 4 + k] -= f * t[i * 4 + k];
                        s[j * 4 + k] -= f * s[i * 4 + k];
                    }
                }
            }

            return new Matrix44(s[0], s[1], s[2], s[3], s[4], s[5], s[6], s[7], s[8], s[9], s[10], s[11], s[12], s[13], s[14], s[15]);
        }

        // Imath::Matrix44<float>::inverse() (the noexcept form)
        private static Matrix44 mat_inverse(Matrix44 m)
        {
            if (m.m03 != 0 || m.m13 != 0 || m.m23 != 0 || m.m33 != 1)
            {
                return mat_gjinverse(m);
            }

            float* sx = stackalloc float[16];
            sx[0] = m.m11 * m.m22 - m.m21 * m.m12;
            sx[1] = m.m21 * m.m02 - m.m01 * m.m22;
            sx[2] = m.m01 * m.m12 - m.m11 * m.m02;
            sx[3] = 0;
            sx[4] = m.m20 * m.m12 - m.m10 * m.m22;
            sx[5] = m.m00 * m.m22 - m.m20 * m.m02;
            sx[6] = m.m10 * m.m02 - m.m00 * m.m12;
            sx[7] = 0;
            sx[8] = m.m10 * m.m21 - m.m20 * m.m11;
            sx[9] = m.m20 * m.m01 - m.m00 * m.m21;
            sx[10] = m.m00 * m.m11 - m.m10 * m.m01;
            sx[11] = 0;
            sx[12] = 0;
            sx[13] = 0;
            sx[14] = 0;
            sx[15] = 1;

            float r = m.m00 * sx[0] + m.m01 * sx[4] + m.m02 * sx[8];

            if (MathF.Abs(r) >= 1)
            {
                for (int i = 0; i < 3; ++i)
                {
                    for (int j = 0; j < 3; ++j)
                    {
                        sx[i * 4 + j] /= r;
                    }
                }
            }
            else
            {
                float mr = MathF.Abs(r) / fmath.float_min;
                for (int i = 0; i < 3; ++i)
                {
                    for (int j = 0; j < 3; ++j)
                    {
                        if (mr > MathF.Abs(sx[i * 4 + j]))
                        {
                            sx[i * 4 + j] /= r;
                        }
                        else
                        {
                            return Matrix44.identity;
                        }
                    }
                }
            }

            sx[12] = -m.m30 * sx[0] - m.m31 * sx[4] - m.m32 * sx[8];
            sx[13] = -m.m30 * sx[1] - m.m31 * sx[5] - m.m32 * sx[9];
            sx[14] = -m.m30 * sx[2] - m.m31 * sx[6] - m.m32 * sx[10];

            return new Matrix44(sx[0], sx[1], sx[2], sx[3], sx[4], sx[5], sx[6], sx[7], sx[8], sx[9], sx[10], sx[11], sx[12], sx[13], sx[14], sx[15]);
        }

        private static float det2x2(float a, float b, float c, float d)
        {
            return a * d - b * c;
        }

        private static float det3x3(float a1, float a2, float a3, float b1, float b2, float b3, float c1, float c2, float c3)
        {
            return a1 * det2x2(b2, b3, c2, c3)
                 - b1 * det2x2(a2, a3, c2, c3)
                 + c1 * det2x2(a2, a3, b2, b3);
        }

        // Imathx det4x4
        private static float det4x4(Matrix44 m)
        {
            float a1 = m.m00, b1 = m.m01, c1 = m.m02, d1 = m.m03;
            float a2 = m.m10, b2 = m.m11, c2 = m.m12, d2 = m.m13;
            float a3 = m.m20, b3 = m.m21, c3 = m.m22, d3 = m.m23;
            float a4 = m.m30, b4 = m.m31, c4 = m.m32, d4 = m.m33;
            return a1 * det3x3(b2, b3, b4, c2, c3, c4, d2, d3, d4)
                 - b1 * det3x3(a2, a3, a4, c2, c3, c4, d2, d3, d4)
                 + c1 * det3x3(a2, a3, a4, b2, b3, b4, d2, d3, d4)
                 - d1 * det3x3(a2, a3, a4, b2, b3, b4, c2, c3, c4);
        }

        // dual_vec.h robust_multVecMatrix: a point times a matrix, with w == 0 giving 0
        private static Vec3 robust_multVecMatrix(Matrix44 M, Vec3 src)
        {
            float a = src.x * M.m00 + src.y * M.m10 + src.z * M.m20 + M.m30;
            float b = src.x * M.m01 + src.y * M.m11 + src.z * M.m21 + M.m31;
            float c = src.x * M.m02 + src.y * M.m12 + src.z * M.m22 + M.m32;
            float w = src.x * M.m03 + src.y * M.m13 + src.z * M.m23 + M.m33;
            if (w != 0.0f)
            {
                return new Vec3(a / w, b / w, c / w);
            }
            return new Vec3(0.0f, 0.0f, 0.0f);
        }

        private static Dual2v3 robust_multVecMatrix(Matrix44 M, Dual2v3 input)
        {
            // Rearrange into a Vec3<Dual<float>>
            Dual2f dinx = DualMath.comp_x(input);
            Dual2f diny = DualMath.comp_y(input);
            Dual2f dinz = DualMath.comp_z(input);

            Dual2f a = dinx * M.m00 + diny * M.m10 + dinz * M.m20 + M.m30;
            Dual2f b = dinx * M.m01 + diny * M.m11 + dinz * M.m21 + M.m31;
            Dual2f c = dinx * M.m02 + diny * M.m12 + dinz * M.m22 + M.m32;
            Dual2f w = dinx * M.m03 + diny * M.m13 + dinz * M.m23 + M.m33;

            Dual2f doutx;
            Dual2f douty;
            Dual2f doutz;
            if (w.x != 0.0f)
            {
                doutx = a / w;
                douty = b / w;
                doutz = c / w;
            }
            else
            {
                doutx = new Dual2f(0.0f);
                douty = new Dual2f(0.0f);
                doutz = new Dual2f(0.0f);
            }

            // Rearrange back into Dual<Vec3>
            return DualMath.make_Vec3(doutx, douty, doutz);
        }

        // Imathx multDirMatrix: a direction times a matrix (no translation, no w)
        private static Vec3 multDirMatrix(Matrix44 M, Vec3 src)
        {
            float a = src.x * M.m00 + src.y * M.m10 + src.z * M.m20;
            float b = src.x * M.m01 + src.y * M.m11 + src.z * M.m21;
            float c = src.x * M.m02 + src.y * M.m12 + src.z * M.m22;
            return new Vec3(a, b, c);
        }

        private static Dual2v3 multDirMatrix(Matrix44 M, Dual2v3 input)
        {
            return new Dual2v3(multDirMatrix(M, input.x), multDirMatrix(M, input.dx), multDirMatrix(M, input.dy));
        }

        // Matrix ops

        public static void osl_mul_mmm(void* r, void* a, void* b)
        {
            *(Matrix44*)r = mat_multiply(*(Matrix44*)a, *(Matrix44*)b);
        }

        public static void osl_mul_mmf(void* r, void* a, float b)
        {
            *(Matrix44*)r = mat_scale(*(Matrix44*)a, b);
        }

        public static void osl_div_mmm(void* r, void* a, void* b)
        {
            *(Matrix44*)r = mat_multiply(*(Matrix44*)a, mat_inverse(*(Matrix44*)b));
        }

        public static void osl_div_mmf(void* r, void* a, float b)
        {
            *(Matrix44*)r = mat_scale(*(Matrix44*)a, 1.0f / b);
        }

        public static void osl_div_mfm(void* r, float a, void* b)
        {
            *(Matrix44*)r = mat_scale(mat_inverse(*(Matrix44*)b), a);
        }

        public static void osl_div_m_ff(void* r, float a, float b)
        {
            float f = (b == 0) ? 0.0f : (a / b);
            *(Matrix44*)r = new Matrix44(f, 0, 0, 0, 0, f, 0, 0, 0, 0, f, 0, 0, 0, 0, f);
        }

        public static void osl_transpose_mm(void* r, void* m)
        {
            *(Matrix44*)r = mat_transposed(*(Matrix44*)m);
        }

        // point = M * point
        public static void osl_transform_vmv(void* result, void* M_, void* v_)
        {
            *(Vec3*)result = robust_multVecMatrix(*(Matrix44*)M_, *(Vec3*)v_);
        }

        public static void osl_transform_dvmdv(void* result, void* M_, void* v_)
        {
            *(Dual2v3*)result = robust_multVecMatrix(*(Matrix44*)M_, *(Dual2v3*)v_);
        }

        // vector = M * vector
        public static void osl_transformv_vmv(void* result, void* M_, void* v_)
        {
            *(Vec3*)result = multDirMatrix(*(Matrix44*)M_, *(Vec3*)v_);
        }

        public static void osl_transformv_dvmdv(void* result, void* M_, void* v_)
        {
            *(Dual2v3*)result = multDirMatrix(*(Matrix44*)M_, *(Dual2v3*)v_);
        }

        // normal = M * normal
        public static void osl_transformn_vmv(void* result, void* M_, void* v_)
        {
            *(Vec3*)result = multDirMatrix(mat_transposed(mat_inverse(*(Matrix44*)M_)), *(Vec3*)v_);
        }

        public static void osl_transformn_dvmdv(void* result, void* M_, void* v_)
        {
            *(Dual2v3*)result = multDirMatrix(mat_transposed(mat_inverse(*(Matrix44*)M_)), *(Dual2v3*)v_);
        }

        public static float osl_determinant_fm(void* m)
        {
            return det4x4(*(Matrix44*)m);
        }
    }
}
