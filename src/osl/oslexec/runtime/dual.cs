// OSL port: src/include/OSL/dual.h, src/include/OSL/dual_vec.h
//
// OSL's Dual2<T> (a value plus its two screen-space partials) is a C++ template. Here it is one concrete struct per instantiation the runtime uses:
// Dual2f is Dual2<float> (3 floats {val,dx,dy}) and Dual2v3 is Dual2<Vec3> (9 floats {val,dx,dy}), laid out exactly like the C++ so shader code can
// hand the runtime pointers to them. Only the "fast math" (OSL_FAST_MATH=1, the default build) flavour of the Dual functions is ported, because that
// is what llvm_ops.cpp selects.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OIIO;

namespace OSL
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Dual2f
    {
        public float x;
        public float dx;
        public float dy;

        public Dual2f(float val)
        {
            x = val;
            dx = 0.0f;
            dy = 0.0f;
        }

        public Dual2f(float val, float dx, float dy)
        {
            this.x = val;
            this.dx = dx;
            this.dy = dy;
        }

        public void set(float val, float dx, float dy)
        {
            this.x = val;
            this.dx = dx;
            this.dy = dy;
        }

        public void clear_d()
        {
            dx = 0.0f;
            dy = 0.0f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Dual2f operator +(Dual2f a, Dual2f b)
        {
            return new Dual2f(a.x + b.x, a.dx + b.dx, a.dy + b.dy);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Dual2f operator +(float a, Dual2f b)
        {
            return new Dual2f(b.x + a, b.dx, b.dy);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Dual2f operator +(Dual2f a, float b)
        {
            return new Dual2f(a.x + b, a.dx, a.dy);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Dual2f operator -(Dual2f a, Dual2f b)
        {
            return new Dual2f(a.x - b.x, a.dx - b.dx, a.dy - b.dy);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Dual2f operator -(Dual2f a, float b)
        {
            return new Dual2f(a.x - b, a.dx, a.dy);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Dual2f operator -(float a, Dual2f b)
        {
            return new Dual2f(a - b.x, -b.dx, -b.dy);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Dual2f operator -(Dual2f a)
        {
            return new Dual2f(-a.x, -a.dx, -a.dy);
        }

        // Multiplication of duals: the chain rule
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Dual2f operator *(Dual2f a, Dual2f b)
        {
            return new Dual2f(a.x * b.x, (float)(a.x * b.dx) + (float)(a.dx * b.x), (float)(a.x * b.dy) + (float)(a.dy * b.x));
        }

        // Multiplication of dual by a non-dual scalar
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Dual2f operator *(Dual2f a, float b)
        {
            return new Dual2f(a.x * b, a.dx * b, a.dy * b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Dual2f operator *(float b, Dual2f a)
        {
            return a * b;
        }

        // Division of duals
        // Vec3 * Dual<float> (a non-dual value times a dual, as Dual<Vec3>)
        public static Dual2v3 operator *(Vec3 b, Dual2f a)
        {
            return new Dual2v3(b * a.x, b * a.dx, b * a.dy);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Dual2f operator /(Dual2f a, Dual2f b)
        {
            float bvalinv = 1.0f / b.x;
            float aval_bval = a.x / b.x;
            return new Dual2f(aval_bval, bvalinv * (a.dx - (float)(aval_bval * b.dx)), bvalinv * (a.dy - (float)(aval_bval * b.dy)));
        }

        // Division of dual by scalar
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Dual2f operator /(Dual2f a, float b)
        {
            float bvalinv = 1.0f / b;
            float aval_bval = a.x / b;
            return new Dual2f(aval_bval, bvalinv * a.dx, bvalinv * a.dy);
        }

        // Division of scalar by dual
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Dual2f operator /(float aval, Dual2f b)
        {
            float bvalinv = 1.0f / b.x;
            float aval_bval = aval / b.x;
            return new Dual2f(aval_bval, bvalinv * (-aval_bval * b.dx), bvalinv * (-aval_bval * b.dy));
        }

        // In-place compound assignment, as Dual's operator+=, -= and *=
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator +=(Dual2f b)
        {
            x += b.x;
            dx += b.dx;
            dy += b.dy;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator +=(float b)
        {
            x += b;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator -=(Dual2f b)
        {
            x -= b.x;
            dx -= b.dx;
            dy -= b.dy;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator -=(float b)
        {
            x -= b;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator *=(float b)
        {
            x *= b;
            dx *= b;
            dy *= b;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Dual2v3
    {
        public Vec3 x;
        public Vec3 dx;
        public Vec3 dy;

        public Dual2v3(Vec3 val)
        {
            x = val;
            dx = new Vec3(0.0f, 0.0f, 0.0f);
            dy = new Vec3(0.0f, 0.0f, 0.0f);
        }

        public Dual2v3(Vec3 val, Vec3 dx, Vec3 dy)
        {
            this.x = val;
            this.dx = dx;
            this.dy = dy;
        }

        public void set(Vec3 val, Vec3 dx, Vec3 dy)
        {
            this.x = val;
            this.dx = dx;
            this.dy = dy;
        }

        public void clear_d()
        {
            dx = new Vec3(0.0f, 0.0f, 0.0f);
            dy = new Vec3(0.0f, 0.0f, 0.0f);
        }

        // Dual<Vec3> + Vec3 only changes the value
        public static Dual2v3 operator +(Dual2v3 a, Vec3 b)
        {
            return new Dual2v3(a.x + b, a.dx, a.dy);
        }

        public static Dual2v3 operator -(Dual2v3 a, Dual2v3 b)
        {
            return new Dual2v3(a.x - b.x, a.dx - b.dx, a.dy - b.dy);
        }

        public static Dual2v3 operator -(Dual2v3 a, Vec3 b)
        {
            return new Dual2v3(a.x - b, a.dx, a.dy);
        }

        public static Dual2v3 operator -(Vec3 a, Dual2v3 b)
        {
            return new Dual2v3(a - b.x, -b.dx, -b.dy);
        }

        // Dual<Vec3> * Vec3 (componentwise) and its commuted form scale each of the three elements
        public static Dual2v3 operator *(Vec3 b, Dual2v3 a)
        {
            return new Dual2v3(a.x * b, a.dx * b, a.dy * b);
        }

        public static Dual2v3 operator *(float b, Dual2v3 a)
        {
            return new Dual2v3(a.x * b, a.dx * b, a.dy * b);
        }

        // Dual<Vec3> + Dual<Vec3>
        public static Dual2v3 operator +(Dual2v3 a, Dual2v3 b)
        {
            return new Dual2v3(a.x + b.x, a.dx + b.dx, a.dy + b.dy);
        }

        // Multiplication of Dual<Vec3> by Dual<float>: the chain rule
        public static Dual2v3 operator *(Dual2v3 a, Dual2f b)
        {
            return new Dual2v3(a.x * b.x, (a.x * b.dx) + (a.dx * b.x), (a.x * b.dy) + (a.dy * b.x));
        }

        public static Dual2v3 operator *(Dual2v3 a, float b)
        {
            return new Dual2v3(a.x * b, a.dx * b, a.dy * b);
        }
    }

    // Free functions on duals (OSL/dual.h and OSL/dual_vec.h), fast-math flavour
    public static class DualMath
    {
        // Swizzle a Dual2<Vec3> into its per-component Dual2<float>s (comp_x, comp_y, comp_z) and back (make_Vec3)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Dual2f comp_x(Dual2v3 v)
        {
            return new Dual2f(v.x.x, v.dx.x, v.dy.x);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Dual2f comp_y(Dual2v3 v)
        {
            return new Dual2f(v.x.y, v.dx.y, v.dy.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Dual2f comp_z(Dual2v3 v)
        {
            return new Dual2f(v.x.z, v.dx.z, v.dy.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Dual2v3 make_Vec3(Dual2f x, Dual2f y, Dual2f z)
        {
            return new Dual2v3(new Vec3(x.x, y.x, z.x), new Vec3(x.dx, y.dx, z.dx), new Vec3(x.dy, y.dy, z.dy));
        }

        // f(u) given the value and derivative: < f(u), f'(u)*u' >
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Dual2f dualfunc(Dual2f u, float f_val, float df_val)
        {
            return new Dual2f(f_val, df_val * u.dx, df_val * u.dy);
        }

        // f(u,v) given the value and partial derivatives: < f(u,v), dfdu(u,v)u' + dfdv(u,v)v' >
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Dual2f dualfunc(Dual2f u, Dual2f v, float f_val, float dfdu_val, float dfdv_val)
        {
            return new Dual2f(f_val, (float)(dfdu_val * u.dx) + (float)(dfdv_val * v.dx), (float)(dfdu_val * u.dy) + (float)(dfdv_val * v.dy));
        }

        // f(u,v,w) given the value and partial derivatives
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Dual2f dualfunc(Dual2f u, Dual2f v, Dual2f w, float f_val, float dfdu_val, float dfdv_val, float dfdw_val)
        {
            return new Dual2f(f_val,
                              (float)((float)(dfdu_val * u.dx) + (float)(dfdv_val * v.dx)) + (float)(dfdw_val * w.dx),
                              (float)((float)(dfdu_val * u.dy) + (float)(dfdv_val * v.dy)) + (float)(dfdw_val * w.dy));
        }

        // f(x) = sin(x), f'(x) = cos(x)  (and the other trig functions)
        public static unsafe Dual2f fast_sin(Dual2f a)
        {
            float sina, cosa;
            fmath.fast_sincos(a.x, &sina, &cosa);
            return dualfunc(a, sina, cosa);
        }

        public static unsafe Dual2f fast_cos(Dual2f a)
        {
            float sina, cosa;
            fmath.fast_sincos(a.x, &sina, &cosa);
            return dualfunc(a, cosa, -sina);
        }

        // f(x) = tan(x), f'(x) = sec^2(x)
        public static Dual2f fast_tan(Dual2f a)
        {
            float tana = fmath.fast_tan(a.x);
            float cosa = fmath.fast_cos(a.x);
            float sec2a = 1 / (cosa * cosa);
            return dualfunc(a, tana, sec2a);
        }

        // f(x) = cosh(x), f'(x) = sinh(x)
        public static Dual2f fast_cosh(Dual2f a)
        {
            float f = fmath.fast_cosh(a.x);
            float df = fmath.fast_sinh(a.x);
            return dualfunc(a, f, df);
        }

        // f(x) = sinh(x), f'(x) = cosh(x)
        public static Dual2f fast_sinh(Dual2f a)
        {
            float f = fmath.fast_sinh(a.x);
            float df = fmath.fast_cosh(a.x);
            return dualfunc(a, f, df);
        }

        // f(x) = tanh(x), f'(x) = sech^2(x)
        public static Dual2f fast_tanh(Dual2f a)
        {
            float tanha = fmath.fast_tanh(a.x);
            float cosha = fmath.fast_cosh(a.x);
            float sech2a = 1.0f / (cosha * cosha);
            return dualfunc(a, tanha, sech2a);
        }

        // f(x) = acos(x), f'(x) = -1/(sqrt(1 - x^2))
        public static Dual2f fast_acos(Dual2f a)
        {
            float f = fmath.fast_acos(a.x);
            float df = MathF.Abs(a.x) < 1.0f ? -1.0f / MathF.Sqrt(1.0f - a.x * a.x) : 0.0f;
            return dualfunc(a, f, df);
        }

        // f(x) = asin(x), f'(x) = 1/(sqrt(1 - x^2))
        public static Dual2f fast_asin(Dual2f a)
        {
            float f = fmath.fast_asin(a.x);
            float df = MathF.Abs(a.x) < 1.0f ? 1.0f / MathF.Sqrt(1.0f - a.x * a.x) : 0.0f;
            return dualfunc(a, f, df);
        }

        // f(x) = atan(x), f'(x) = 1/(1 + x^2)
        public static Dual2f fast_atan(Dual2f a)
        {
            float f = fmath.fast_atan(a.x);
            float df = 1.0f / (1.0f + a.x * a.x);
            return dualfunc(a, f, df);
        }

        // f(x,y) = atan2(y,x); f'(x) = y x' / (x^2 + y^2), f'(y) = -x y' / (x^2 + y^2)
        public static Dual2f fast_atan2(Dual2f y, Dual2f x)
        {
            float atan2xy = fmath.fast_atan2(y.x, x.x);
            float denom = (x.x == 0 && y.x == 0) ? 0.0f : 1.0f / (x.x * x.x + y.x * y.x);
            return dualfunc(y, x, atan2xy, -x.x * denom, y.x * denom);
        }

        // f(x) = log(a), f'(x) = 1/x
        public static Dual2f fast_log(Dual2f a)
        {
            float f = fmath.fast_log(a.x);
            float df = a.x < fmath.float_min ? 0.0f : 1.0f / a.x;
            return dualfunc(a, f, df);
        }

        // pow(u,v) = < u^v, vu^(v-1) u' + log(u)u^v v' >
        public static Dual2f fast_safe_pow(Dual2f u, Dual2f v)
        {
            float powuvm1 = fmath.fast_safe_pow(u.x, v.x - 1.0f);
            float powuv = powuvm1 * u.x;
            float logu = u.x > 0 ? fmath.fast_log(u.x) : 0.0f;
            return dualfunc(u, v, powuv, v.x * powuvm1, logu * powuv);
        }

        // OSL::safe_pow(Dual, Dual): pow(u,v) = < u^v, vu^(v-1) u' + log(u)u^v v' >, via the identity u^v = u * u^(v-1)
        public static Dual2f safe_pow(Dual2f u, Dual2f v)
        {
            float powuvm1 = fmath.safe_pow(u.x, v.x - 1.0f);
            float powuv = powuvm1 * u.x;
            float logu = u.x > 0 ? fmath.safe_log(u.x) : 0.0f;
            return dualfunc(u, v, powuv, v.x * powuvm1, logu * powuv);
        }

        // f(x) = log2(x), f'(x) = 1/(x*log2)
        public static Dual2f fast_log2(Dual2f a)
        {
            float f = fmath.fast_log2(a.x);
            float aln2 = a.x * (float)fmath.M_LN2;
            float df = aln2 < fmath.float_min ? 0.0f : 1.0f / aln2;
            return dualfunc(a, f, df);
        }

        // f(x) = log10(x), f'(x) = 1/(x*log10)
        public static Dual2f fast_log10(Dual2f a)
        {
            float f = fmath.fast_log10(a.x);
            float aln10 = a.x * (float)fmath.M_LN10;
            float df = aln10 < fmath.float_min ? 0.0f : 1.0f / aln10;
            return dualfunc(a, f, df);
        }

        // f(x) = e^x, f'(x) = e^x
        public static Dual2f fast_exp(Dual2f a)
        {
            float f = fmath.fast_exp(a.x);
            return dualfunc(a, f, f);
        }

        // f(x) = 2^x, f'(x) = (2^x)*log(2)
        public static Dual2f fast_exp2(Dual2f a)
        {
            float f = fmath.fast_exp2(a.x);
            return dualfunc(a, f, f * (float)fmath.M_LN2);
        }

        // f(x) = e^x - 1, f'(x) = e^x
        public static Dual2f fast_expm1(Dual2f a)
        {
            float f = fmath.fast_expm1(a.x);
            float df = fmath.fast_exp(a.x);
            return dualfunc(a, f, df);
        }

        // f(x) = erf(x), f'(x) = (2e^(-x^2))/sqrt(pi)
        public static Dual2f fast_erf(Dual2f a)
        {
            float f = fmath.fast_erf(a.x);
            float two_over_sqrt_pi = 1.128379167095512573896158903f;
            float df = fmath.fast_exp(-a.x * a.x) * two_over_sqrt_pi;
            return dualfunc(a, f, df);
        }

        // f(x) = erfc(x), f'(x) = -(2e^(-x^2))/sqrt(pi)
        public static Dual2f fast_erfc(Dual2f a)
        {
            float f = fmath.fast_erfc(a.x);
            float two_over_sqrt_pi = -1.128379167095512573896158903f;
            float df = fmath.fast_exp(-a.x * a.x) * two_over_sqrt_pi;
            return dualfunc(a, f, df);
        }

        // f(x) = cbrt(x), f'(x) = 1/(3*x^(2/3))
        public static Dual2f fast_cbrt(Dual2f a)
        {
            if (a.x != 0.0f)
            {
                float f = fmath.fast_cbrt(a.x);
                float df = 1.0f / (3.0f * f * f);
                return dualfunc(a, f, df);
            }
            return new Dual2f(0.0f);
        }

        // f(x) = sqrt(x), f'(x) = 1/(2*sqrt(x))
        public static Dual2f sqrt(Dual2f a)
        {
            Dual2f result;
            if (a.x > 0.0f)
            {
                float f = MathF.Sqrt(a.x);
                float df = 0.5f / f;
                result = dualfunc(a, f, df);
            }
            else
            {
                result = new Dual2f(0.0f);
            }
            return result;
        }

        // f(x) = 1/sqrt(x), f'(x) = -1/(2*x^(3/2))
        public static Dual2f inversesqrt(Dual2f a)
        {
            Dual2f result;
            if (a.x > 0.0f)
            {
                float f = 1.0f / MathF.Sqrt(a.x);
                float df = -0.5f * f / a.x;
                result = dualfunc(a, f, df);
            }
            else
            {
                result = new Dual2f(0.0f);
            }
            return result;
        }

        // fabs of a dual (llvm_ops.cpp's fabsf(Dual2<float>))
        public static Dual2f fabsf(Dual2f x)
        {
            return x.x >= 0 ? x : -x;
        }

        // safe_fmod keeps the derivatives of a
        public static Dual2f safe_fmod(Dual2f a, Dual2f b)
        {
            Dual2f result = a;
            result.x = fmath.safe_fmod(a.x, b.x);
            return result;
        }

        public static float smoothstep(float e0, float e1, float x)
        {
            if (x < e0)
            {
                return 0.0f;
            }
            else if (x >= e1)
            {
                return 1.0f;
            }
            else
            {
                float t = (x - e0) / (e1 - e0);
                return (3.0f - 2.0f * t) * (t * t);
            }
        }

        // f(t) = (3-2t)t^2,   t = (x-e0)/(e1-e0)
        public static Dual2f smoothstep(Dual2f e0, Dual2f e1, Dual2f x)
        {
            if (x.x < e0.x)
            {
                return new Dual2f(0.0f);
            }
            else if (x.x >= e1.x)
            {
                return new Dual2f(1.0f);
            }
            Dual2f t = (x - e0) / (e1 - e0);
            return (3.0f - 2.0f * t) * t * t;
        }

        // dot, cross, length, normalize and distance of Dual2<Vec3>
        public static Dual2f dot(Dual2v3 a, Dual2v3 b)
        {
            Dual2f ax = comp_x(a);
            Dual2f ay = comp_y(a);
            Dual2f az = comp_z(a);
            Dual2f bx = comp_x(b);
            Dual2f by = comp_y(b);
            Dual2f bz = comp_z(b);
            return ax * bx + ay * by + az * bz;
        }

        public static Dual2v3 cross(Dual2v3 a, Dual2v3 b)
        {
            Dual2f ax = comp_x(a);
            Dual2f ay = comp_y(a);
            Dual2f az = comp_z(a);
            Dual2f bx = comp_x(b);
            Dual2f by = comp_y(b);
            Dual2f bz = comp_z(b);
            Dual2f nx = ay * bz - az * by;
            Dual2f ny = az * bx - ax * bz;
            Dual2f nz = ax * by - ay * bx;
            return make_Vec3(nx, ny, nz);
        }

        public static Dual2f length(Dual2v3 a)
        {
            Dual2f ax = comp_x(a);
            Dual2f ay = comp_y(a);
            Dual2f az = comp_z(a);
            return sqrt(ax * ax + ay * ay + az * az);
        }

        public static Dual2v3 normalize(Dual2v3 a)
        {
            Dual2f ax = comp_x(a);
            Dual2f ay = comp_y(a);
            Dual2f az = comp_z(a);
            Dual2f len = sqrt(ax * ax + ay * ay + az * az);
            if (len.x > 0.0f)
            {
                Dual2f invlen = 1.0f / len;
                Dual2f nax = ax * invlen;
                Dual2f nay = ay * invlen;
                Dual2f naz = az * invlen;
                return make_Vec3(nax, nay, naz);
            }
            else
            {
                return new Dual2v3(new Vec3(0.0f, 0.0f, 0.0f));
            }
        }

        public static Dual2f distance(Dual2v3 a, Dual2v3 b)
        {
            return length(a - b);
        }

        public static Dual2f distance(Dual2v3 a, Vec3 b)
        {
            return length(a - b);
        }

        public static Dual2f distance(Vec3 a, Dual2v3 b)
        {
            return length(a - b);
        }
    }
}
