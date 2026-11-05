// OSL port: src/liboslexec/llvm_ops.cpp (the pure-math shadeops; the ShadingContext ones -- osl_calculatenormal, osl_raytype_bit, osl_range_check -- are not ported)
//
// llvm_ops.cpp builds the polymorphic shadeops (float, vector, with and without derivatives) out of the MAKE_UNARY_PERCOMPONENT_OP,
// MAKE_BINARY_PERCOMPONENT_OP and MAKE_BINARY_PERCOMPONENT_VF_OP macros; those are expanded here, one function per macro-generated name, keeping
// the C++ names (osl_sin_dfdf, osl_pow_dvdvdf ...) because the IL backend binds to them by name. Pointer arguments are the same memory layouts the C++
// uses: float (value), Dual2f = 3 floats, Vec3/Color3 = 3 floats, Dual2v3 = 9 floats. The math is the OSL_FAST_MATH=1 flavour (OIIO::fast_*).

using System;
using System.Runtime.CompilerServices;
using OIIO;

namespace OSL
{
    public static unsafe partial class OslRuntime
    {
        // MAKE_UNARY_PERCOMPONENT_OP(sin)
        public static float osl_sin_ff(float a)
        {
            return fmath.fast_sin(a);
        }

        public static void osl_sin_dfdf(void* r, void* a)
        {
            *(Dual2f*)r = DualMath.fast_sin(*(Dual2f*)a);
        }

        public static void osl_sin_vv(void* r_, void* a_)
        {
            Vec3* r = (Vec3*)r_;
            Vec3* a = (Vec3*)a_;
            r->x = fmath.fast_sin(a->x);
            r->y = fmath.fast_sin(a->y);
            r->z = fmath.fast_sin(a->z);
        }

        public static void osl_sin_dvdv(void* r_, void* a_)
        {
            Dual2v3* r = (Dual2v3*)r_;
            Dual2v3* a = (Dual2v3*)a_;
            // Swizzle the Dual2<Vec3>'s into 3 Dual2<float>'s
            Dual2f ax = DualMath.fast_sin(new Dual2f(a->x.x, a->dx.x, a->dy.x));
            Dual2f ay = DualMath.fast_sin(new Dual2f(a->x.y, a->dx.y, a->dy.y));
            Dual2f az = DualMath.fast_sin(new Dual2f(a->x.z, a->dx.z, a->dy.z));
            // Now swizzle back
            r->set(new Vec3(ax.x, ay.x, az.x), new Vec3(ax.dx, ay.dx, az.dx), new Vec3(ax.dy, ay.dy, az.dy));
        }

        // MAKE_UNARY_PERCOMPONENT_OP(cos)
        public static float osl_cos_ff(float a)
        {
            return fmath.fast_cos(a);
        }

        public static void osl_cos_dfdf(void* r, void* a)
        {
            *(Dual2f*)r = DualMath.fast_cos(*(Dual2f*)a);
        }

        public static void osl_cos_vv(void* r_, void* a_)
        {
            Vec3* r = (Vec3*)r_;
            Vec3* a = (Vec3*)a_;
            r->x = fmath.fast_cos(a->x);
            r->y = fmath.fast_cos(a->y);
            r->z = fmath.fast_cos(a->z);
        }

        public static void osl_cos_dvdv(void* r_, void* a_)
        {
            Dual2v3* r = (Dual2v3*)r_;
            Dual2v3* a = (Dual2v3*)a_;
            // Swizzle the Dual2<Vec3>'s into 3 Dual2<float>'s
            Dual2f ax = DualMath.fast_cos(new Dual2f(a->x.x, a->dx.x, a->dy.x));
            Dual2f ay = DualMath.fast_cos(new Dual2f(a->x.y, a->dx.y, a->dy.y));
            Dual2f az = DualMath.fast_cos(new Dual2f(a->x.z, a->dx.z, a->dy.z));
            // Now swizzle back
            r->set(new Vec3(ax.x, ay.x, az.x), new Vec3(ax.dx, ay.dx, az.dx), new Vec3(ax.dy, ay.dy, az.dy));
        }

        // MAKE_UNARY_PERCOMPONENT_OP(tan)
        public static float osl_tan_ff(float a)
        {
            return fmath.fast_tan(a);
        }

        public static void osl_tan_dfdf(void* r, void* a)
        {
            *(Dual2f*)r = DualMath.fast_tan(*(Dual2f*)a);
        }

        public static void osl_tan_vv(void* r_, void* a_)
        {
            Vec3* r = (Vec3*)r_;
            Vec3* a = (Vec3*)a_;
            r->x = fmath.fast_tan(a->x);
            r->y = fmath.fast_tan(a->y);
            r->z = fmath.fast_tan(a->z);
        }

        public static void osl_tan_dvdv(void* r_, void* a_)
        {
            Dual2v3* r = (Dual2v3*)r_;
            Dual2v3* a = (Dual2v3*)a_;
            // Swizzle the Dual2<Vec3>'s into 3 Dual2<float>'s
            Dual2f ax = DualMath.fast_tan(new Dual2f(a->x.x, a->dx.x, a->dy.x));
            Dual2f ay = DualMath.fast_tan(new Dual2f(a->x.y, a->dx.y, a->dy.y));
            Dual2f az = DualMath.fast_tan(new Dual2f(a->x.z, a->dx.z, a->dy.z));
            // Now swizzle back
            r->set(new Vec3(ax.x, ay.x, az.x), new Vec3(ax.dx, ay.dx, az.dx), new Vec3(ax.dy, ay.dy, az.dy));
        }

        // MAKE_UNARY_PERCOMPONENT_OP(asin)
        public static float osl_asin_ff(float a)
        {
            return fmath.fast_asin(a);
        }

        public static void osl_asin_dfdf(void* r, void* a)
        {
            *(Dual2f*)r = DualMath.fast_asin(*(Dual2f*)a);
        }

        public static void osl_asin_vv(void* r_, void* a_)
        {
            Vec3* r = (Vec3*)r_;
            Vec3* a = (Vec3*)a_;
            r->x = fmath.fast_asin(a->x);
            r->y = fmath.fast_asin(a->y);
            r->z = fmath.fast_asin(a->z);
        }

        public static void osl_asin_dvdv(void* r_, void* a_)
        {
            Dual2v3* r = (Dual2v3*)r_;
            Dual2v3* a = (Dual2v3*)a_;
            // Swizzle the Dual2<Vec3>'s into 3 Dual2<float>'s
            Dual2f ax = DualMath.fast_asin(new Dual2f(a->x.x, a->dx.x, a->dy.x));
            Dual2f ay = DualMath.fast_asin(new Dual2f(a->x.y, a->dx.y, a->dy.y));
            Dual2f az = DualMath.fast_asin(new Dual2f(a->x.z, a->dx.z, a->dy.z));
            // Now swizzle back
            r->set(new Vec3(ax.x, ay.x, az.x), new Vec3(ax.dx, ay.dx, az.dx), new Vec3(ax.dy, ay.dy, az.dy));
        }

        // MAKE_UNARY_PERCOMPONENT_OP(acos)
        public static float osl_acos_ff(float a)
        {
            return fmath.fast_acos(a);
        }

        public static void osl_acos_dfdf(void* r, void* a)
        {
            *(Dual2f*)r = DualMath.fast_acos(*(Dual2f*)a);
        }

        public static void osl_acos_vv(void* r_, void* a_)
        {
            Vec3* r = (Vec3*)r_;
            Vec3* a = (Vec3*)a_;
            r->x = fmath.fast_acos(a->x);
            r->y = fmath.fast_acos(a->y);
            r->z = fmath.fast_acos(a->z);
        }

        public static void osl_acos_dvdv(void* r_, void* a_)
        {
            Dual2v3* r = (Dual2v3*)r_;
            Dual2v3* a = (Dual2v3*)a_;
            // Swizzle the Dual2<Vec3>'s into 3 Dual2<float>'s
            Dual2f ax = DualMath.fast_acos(new Dual2f(a->x.x, a->dx.x, a->dy.x));
            Dual2f ay = DualMath.fast_acos(new Dual2f(a->x.y, a->dx.y, a->dy.y));
            Dual2f az = DualMath.fast_acos(new Dual2f(a->x.z, a->dx.z, a->dy.z));
            // Now swizzle back
            r->set(new Vec3(ax.x, ay.x, az.x), new Vec3(ax.dx, ay.dx, az.dx), new Vec3(ax.dy, ay.dy, az.dy));
        }

        // MAKE_UNARY_PERCOMPONENT_OP(atan)
        public static float osl_atan_ff(float a)
        {
            return fmath.fast_atan(a);
        }

        public static void osl_atan_dfdf(void* r, void* a)
        {
            *(Dual2f*)r = DualMath.fast_atan(*(Dual2f*)a);
        }

        public static void osl_atan_vv(void* r_, void* a_)
        {
            Vec3* r = (Vec3*)r_;
            Vec3* a = (Vec3*)a_;
            r->x = fmath.fast_atan(a->x);
            r->y = fmath.fast_atan(a->y);
            r->z = fmath.fast_atan(a->z);
        }

        public static void osl_atan_dvdv(void* r_, void* a_)
        {
            Dual2v3* r = (Dual2v3*)r_;
            Dual2v3* a = (Dual2v3*)a_;
            // Swizzle the Dual2<Vec3>'s into 3 Dual2<float>'s
            Dual2f ax = DualMath.fast_atan(new Dual2f(a->x.x, a->dx.x, a->dy.x));
            Dual2f ay = DualMath.fast_atan(new Dual2f(a->x.y, a->dx.y, a->dy.y));
            Dual2f az = DualMath.fast_atan(new Dual2f(a->x.z, a->dx.z, a->dy.z));
            // Now swizzle back
            r->set(new Vec3(ax.x, ay.x, az.x), new Vec3(ax.dx, ay.dx, az.dx), new Vec3(ax.dy, ay.dy, az.dy));
        }

        // MAKE_BINARY_PERCOMPONENT_OP(atan2)
        public static float osl_atan2_fff(float a, float b)
        {
            return fmath.fast_atan2(a, b);
        }

        public static void osl_atan2_dfdfdf(void* r, void* a, void* b)
        {
            *(Dual2f*)r = DualMath.fast_atan2(*(Dual2f*)a, *(Dual2f*)b);
        }

        public static void osl_atan2_dffdf(void* r, float a, void* b)
        {
            *(Dual2f*)r = DualMath.fast_atan2(new Dual2f(a), *(Dual2f*)b);
        }

        public static void osl_atan2_dfdff(void* r, void* a, float b)
        {
            *(Dual2f*)r = DualMath.fast_atan2(*(Dual2f*)a, new Dual2f(b));
        }

        public static void osl_atan2_vvv(void* r_, void* a_, void* b_)
        {
            Vec3* r = (Vec3*)r_;
            Vec3* a = (Vec3*)a_;
            Vec3* b = (Vec3*)b_;
            r->x = fmath.fast_atan2(a->x, b->x);
            r->y = fmath.fast_atan2(a->y, b->y);
            r->z = fmath.fast_atan2(a->z, b->z);
        }

        public static void osl_atan2_dvdvdv(void* r_, void* a_, void* b_)
        {
            Dual2v3* r = (Dual2v3*)r_;
            Dual2v3* a = (Dual2v3*)a_;
            Dual2v3* b = (Dual2v3*)b_;
            // Swizzle the Dual2<Vec3>'s into 3 Dual2<float>'s
            Dual2f ax = DualMath.fast_atan2(new Dual2f(a->x.x, a->dx.x, a->dy.x), new Dual2f(b->x.x, b->dx.x, b->dy.x));
            Dual2f ay = DualMath.fast_atan2(new Dual2f(a->x.y, a->dx.y, a->dy.y), new Dual2f(b->x.y, b->dx.y, b->dy.y));
            Dual2f az = DualMath.fast_atan2(new Dual2f(a->x.z, a->dx.z, a->dy.z), new Dual2f(b->x.z, b->dx.z, b->dy.z));
            // Now swizzle back
            r->set(new Vec3(ax.x, ay.x, az.x), new Vec3(ax.dx, ay.dx, az.dx), new Vec3(ax.dy, ay.dy, az.dy));
        }

        public static void osl_atan2_dvvdv(void* r_, void* a_, void* b_)
        {
            Dual2v3 a = new Dual2v3(*(Vec3*)a_);
            osl_atan2_dvdvdv(r_, &a, b_);
        }

        public static void osl_atan2_dvdvv(void* r_, void* a_, void* b_)
        {
            Dual2v3 b = new Dual2v3(*(Vec3*)b_);
            osl_atan2_dvdvdv(r_, a_, &b);
        }

        // MAKE_UNARY_PERCOMPONENT_OP(sinh)
        public static float osl_sinh_ff(float a)
        {
            return fmath.fast_sinh(a);
        }

        public static void osl_sinh_dfdf(void* r, void* a)
        {
            *(Dual2f*)r = DualMath.fast_sinh(*(Dual2f*)a);
        }

        public static void osl_sinh_vv(void* r_, void* a_)
        {
            Vec3* r = (Vec3*)r_;
            Vec3* a = (Vec3*)a_;
            r->x = fmath.fast_sinh(a->x);
            r->y = fmath.fast_sinh(a->y);
            r->z = fmath.fast_sinh(a->z);
        }

        public static void osl_sinh_dvdv(void* r_, void* a_)
        {
            Dual2v3* r = (Dual2v3*)r_;
            Dual2v3* a = (Dual2v3*)a_;
            // Swizzle the Dual2<Vec3>'s into 3 Dual2<float>'s
            Dual2f ax = DualMath.fast_sinh(new Dual2f(a->x.x, a->dx.x, a->dy.x));
            Dual2f ay = DualMath.fast_sinh(new Dual2f(a->x.y, a->dx.y, a->dy.y));
            Dual2f az = DualMath.fast_sinh(new Dual2f(a->x.z, a->dx.z, a->dy.z));
            // Now swizzle back
            r->set(new Vec3(ax.x, ay.x, az.x), new Vec3(ax.dx, ay.dx, az.dx), new Vec3(ax.dy, ay.dy, az.dy));
        }

        // MAKE_UNARY_PERCOMPONENT_OP(cosh)
        public static float osl_cosh_ff(float a)
        {
            return fmath.fast_cosh(a);
        }

        public static void osl_cosh_dfdf(void* r, void* a)
        {
            *(Dual2f*)r = DualMath.fast_cosh(*(Dual2f*)a);
        }

        public static void osl_cosh_vv(void* r_, void* a_)
        {
            Vec3* r = (Vec3*)r_;
            Vec3* a = (Vec3*)a_;
            r->x = fmath.fast_cosh(a->x);
            r->y = fmath.fast_cosh(a->y);
            r->z = fmath.fast_cosh(a->z);
        }

        public static void osl_cosh_dvdv(void* r_, void* a_)
        {
            Dual2v3* r = (Dual2v3*)r_;
            Dual2v3* a = (Dual2v3*)a_;
            // Swizzle the Dual2<Vec3>'s into 3 Dual2<float>'s
            Dual2f ax = DualMath.fast_cosh(new Dual2f(a->x.x, a->dx.x, a->dy.x));
            Dual2f ay = DualMath.fast_cosh(new Dual2f(a->x.y, a->dx.y, a->dy.y));
            Dual2f az = DualMath.fast_cosh(new Dual2f(a->x.z, a->dx.z, a->dy.z));
            // Now swizzle back
            r->set(new Vec3(ax.x, ay.x, az.x), new Vec3(ax.dx, ay.dx, az.dx), new Vec3(ax.dy, ay.dy, az.dy));
        }

        // MAKE_UNARY_PERCOMPONENT_OP(tanh)
        public static float osl_tanh_ff(float a)
        {
            return fmath.fast_tanh(a);
        }

        public static void osl_tanh_dfdf(void* r, void* a)
        {
            *(Dual2f*)r = DualMath.fast_tanh(*(Dual2f*)a);
        }

        public static void osl_tanh_vv(void* r_, void* a_)
        {
            Vec3* r = (Vec3*)r_;
            Vec3* a = (Vec3*)a_;
            r->x = fmath.fast_tanh(a->x);
            r->y = fmath.fast_tanh(a->y);
            r->z = fmath.fast_tanh(a->z);
        }

        public static void osl_tanh_dvdv(void* r_, void* a_)
        {
            Dual2v3* r = (Dual2v3*)r_;
            Dual2v3* a = (Dual2v3*)a_;
            // Swizzle the Dual2<Vec3>'s into 3 Dual2<float>'s
            Dual2f ax = DualMath.fast_tanh(new Dual2f(a->x.x, a->dx.x, a->dy.x));
            Dual2f ay = DualMath.fast_tanh(new Dual2f(a->x.y, a->dx.y, a->dy.y));
            Dual2f az = DualMath.fast_tanh(new Dual2f(a->x.z, a->dx.z, a->dy.z));
            // Now swizzle back
            r->set(new Vec3(ax.x, ay.x, az.x), new Vec3(ax.dx, ay.dx, az.dx), new Vec3(ax.dy, ay.dy, az.dy));
        }

        public static void osl_sincos_fff(float x, void* s_, void* c_)
        {
            fmath.fast_sincos(x, (float*)s_, (float*)c_);
        }

        public static void osl_sincos_dfdff(void* x_, void* s_, void* c_)
        {
            Dual2f* x = (Dual2f*)x_;
            Dual2f* sine = (Dual2f*)s_;
            float* cosine = (float*)c_;

            float s_f, c_f;
            fmath.fast_sincos(x->x, &s_f, &c_f);

            float xdx = x->dx, xdy = x->dy; // x might be aliased
            *sine = new Dual2f(s_f, c_f * xdx, c_f * xdy);
            *cosine = c_f;
        }

        public static void osl_sincos_dffdf(void* x_, void* s_, void* c_)
        {
            Dual2f* x = (Dual2f*)x_;
            float* sine = (float*)s_;
            Dual2f* cosine = (Dual2f*)c_;

            float s_f, c_f;
            fmath.fast_sincos(x->x, &s_f, &c_f);
            float xdx = x->dx, xdy = x->dy; // x might be aliased
            *sine = s_f;
            *cosine = new Dual2f(c_f, -s_f * xdx, -s_f * xdy);
        }

        public static void osl_sincos_dfdfdf(void* x_, void* s_, void* c_)
        {
            Dual2f* x = (Dual2f*)x_;
            Dual2f* sine = (Dual2f*)s_;
            Dual2f* cosine = (Dual2f*)c_;

            float s_f, c_f;
            fmath.fast_sincos(x->x, &s_f, &c_f);
            float xdx = x->dx, xdy = x->dy; // x might be aliased
            *sine = new Dual2f(s_f, c_f * xdx, c_f * xdy);
            *cosine = new Dual2f(c_f, -s_f * xdx, -s_f * xdy);
        }

        public static void osl_sincos_vvv(void* x_, void* s_, void* c_)
        {
            Vec3* x = (Vec3*)x_;
            Vec3* s = (Vec3*)s_;
            Vec3* c = (Vec3*)c_;
            fmath.fast_sincos(x->x, &s->x, &c->x);
            fmath.fast_sincos(x->y, &s->y, &c->y);
            fmath.fast_sincos(x->z, &s->z, &c->z);
        }

        // Helpers of the sincos shadeops that take a Dual2<Vec3>, one component at a time (the lambdas sincos_comp in llvm_ops.cpp)
        private static void sincos_comp_sine(Dual2f x, float* sine_val, float* sine_dx, float* sine_dy, float* cosine_val)
        {
            float s_f, c_f;
            fmath.fast_sincos(x.x, &s_f, &c_f);
            *sine_val = s_f;
            *sine_dx = c_f * x.dx;
            *sine_dy = c_f * x.dy;
            *cosine_val = c_f;
        }

        private static void sincos_comp_cosine(Dual2f x, float* sine_val, float* cosine_val, float* cosine_dx, float* cosine_dy)
        {
            float s_f, c_f;
            fmath.fast_sincos(x.x, &s_f, &c_f);
            *sine_val = s_f;
            *cosine_val = c_f;
            *cosine_dx = -s_f * x.dx;
            *cosine_dy = -s_f * x.dy;
        }

        private static void sincos_comp_both(Dual2f x, float* sine_val, float* sine_dx, float* sine_dy, float* cosine_val, float* cosine_dx, float* cosine_dy)
        {
            float s_f, c_f;
            fmath.fast_sincos(x.x, &s_f, &c_f);
            *sine_val = s_f;
            *sine_dx = c_f * x.dx;
            *sine_dy = c_f * x.dy;
            *cosine_val = c_f;
            *cosine_dx = -s_f * x.dx;
            *cosine_dy = -s_f * x.dy;
        }

        public static void osl_sincos_dvdvv(void* x_, void* s_, void* c_)
        {
            Dual2v3* x = (Dual2v3*)x_;
            Dual2v3* sine = (Dual2v3*)s_;
            Vec3* cosine = (Vec3*)c_;

            sincos_comp_sine(DualMath.comp_x(*x), &sine->x.x, &sine->dx.x, &sine->dy.x, &cosine->x);
            sincos_comp_sine(DualMath.comp_y(*x), &sine->x.y, &sine->dx.y, &sine->dy.y, &cosine->y);
            sincos_comp_sine(DualMath.comp_z(*x), &sine->x.z, &sine->dx.z, &sine->dy.z, &cosine->z);
        }

        public static void osl_sincos_dvvdv(void* x_, void* s_, void* c_)
        {
            Dual2v3* x = (Dual2v3*)x_;
            Vec3* sine = (Vec3*)s_;
            Dual2v3* cosine = (Dual2v3*)c_;

            sincos_comp_cosine(DualMath.comp_x(*x), &sine->x, &cosine->x.x, &cosine->dx.x, &cosine->dy.x);
            sincos_comp_cosine(DualMath.comp_y(*x), &sine->y, &cosine->x.y, &cosine->dx.y, &cosine->dy.y);
            sincos_comp_cosine(DualMath.comp_z(*x), &sine->z, &cosine->x.z, &cosine->dx.z, &cosine->dy.z);
        }

        public static void osl_sincos_dvdvdv(void* x_, void* s_, void* c_)
        {
            Dual2v3* x = (Dual2v3*)x_;
            Dual2v3* sine = (Dual2v3*)s_;
            Dual2v3* cosine = (Dual2v3*)c_;

            sincos_comp_both(DualMath.comp_x(*x), &sine->x.x, &sine->dx.x, &sine->dy.x, &cosine->x.x, &cosine->dx.x, &cosine->dy.x);
            sincos_comp_both(DualMath.comp_y(*x), &sine->x.y, &sine->dx.y, &sine->dy.y, &cosine->x.y, &cosine->dx.y, &cosine->dy.y);
            sincos_comp_both(DualMath.comp_z(*x), &sine->x.z, &sine->dx.z, &sine->dy.z, &cosine->x.z, &cosine->dx.z, &cosine->dy.z);
        }

        // MAKE_UNARY_PERCOMPONENT_OP(log)
        public static float osl_log_ff(float a)
        {
            return fmath.fast_log(a);
        }

        public static void osl_log_dfdf(void* r, void* a)
        {
            *(Dual2f*)r = DualMath.fast_log(*(Dual2f*)a);
        }

        public static void osl_log_vv(void* r_, void* a_)
        {
            Vec3* r = (Vec3*)r_;
            Vec3* a = (Vec3*)a_;
            r->x = fmath.fast_log(a->x);
            r->y = fmath.fast_log(a->y);
            r->z = fmath.fast_log(a->z);
        }

        public static void osl_log_dvdv(void* r_, void* a_)
        {
            Dual2v3* r = (Dual2v3*)r_;
            Dual2v3* a = (Dual2v3*)a_;
            // Swizzle the Dual2<Vec3>'s into 3 Dual2<float>'s
            Dual2f ax = DualMath.fast_log(new Dual2f(a->x.x, a->dx.x, a->dy.x));
            Dual2f ay = DualMath.fast_log(new Dual2f(a->x.y, a->dx.y, a->dy.y));
            Dual2f az = DualMath.fast_log(new Dual2f(a->x.z, a->dx.z, a->dy.z));
            // Now swizzle back
            r->set(new Vec3(ax.x, ay.x, az.x), new Vec3(ax.dx, ay.dx, az.dx), new Vec3(ax.dy, ay.dy, az.dy));
        }

        // MAKE_UNARY_PERCOMPONENT_OP(log2)
        public static float osl_log2_ff(float a)
        {
            return fmath.fast_log2(a);
        }

        public static void osl_log2_dfdf(void* r, void* a)
        {
            *(Dual2f*)r = DualMath.fast_log2(*(Dual2f*)a);
        }

        public static void osl_log2_vv(void* r_, void* a_)
        {
            Vec3* r = (Vec3*)r_;
            Vec3* a = (Vec3*)a_;
            r->x = fmath.fast_log2(a->x);
            r->y = fmath.fast_log2(a->y);
            r->z = fmath.fast_log2(a->z);
        }

        public static void osl_log2_dvdv(void* r_, void* a_)
        {
            Dual2v3* r = (Dual2v3*)r_;
            Dual2v3* a = (Dual2v3*)a_;
            // Swizzle the Dual2<Vec3>'s into 3 Dual2<float>'s
            Dual2f ax = DualMath.fast_log2(new Dual2f(a->x.x, a->dx.x, a->dy.x));
            Dual2f ay = DualMath.fast_log2(new Dual2f(a->x.y, a->dx.y, a->dy.y));
            Dual2f az = DualMath.fast_log2(new Dual2f(a->x.z, a->dx.z, a->dy.z));
            // Now swizzle back
            r->set(new Vec3(ax.x, ay.x, az.x), new Vec3(ax.dx, ay.dx, az.dx), new Vec3(ax.dy, ay.dy, az.dy));
        }

        // MAKE_UNARY_PERCOMPONENT_OP(log10)
        public static float osl_log10_ff(float a)
        {
            return fmath.fast_log10(a);
        }

        public static void osl_log10_dfdf(void* r, void* a)
        {
            *(Dual2f*)r = DualMath.fast_log10(*(Dual2f*)a);
        }

        public static void osl_log10_vv(void* r_, void* a_)
        {
            Vec3* r = (Vec3*)r_;
            Vec3* a = (Vec3*)a_;
            r->x = fmath.fast_log10(a->x);
            r->y = fmath.fast_log10(a->y);
            r->z = fmath.fast_log10(a->z);
        }

        public static void osl_log10_dvdv(void* r_, void* a_)
        {
            Dual2v3* r = (Dual2v3*)r_;
            Dual2v3* a = (Dual2v3*)a_;
            // Swizzle the Dual2<Vec3>'s into 3 Dual2<float>'s
            Dual2f ax = DualMath.fast_log10(new Dual2f(a->x.x, a->dx.x, a->dy.x));
            Dual2f ay = DualMath.fast_log10(new Dual2f(a->x.y, a->dx.y, a->dy.y));
            Dual2f az = DualMath.fast_log10(new Dual2f(a->x.z, a->dx.z, a->dy.z));
            // Now swizzle back
            r->set(new Vec3(ax.x, ay.x, az.x), new Vec3(ax.dx, ay.dx, az.dx), new Vec3(ax.dy, ay.dy, az.dy));
        }

        // MAKE_UNARY_PERCOMPONENT_OP(exp)
        public static float osl_exp_ff(float a)
        {
            return fmath.fast_exp(a);
        }

        public static void osl_exp_dfdf(void* r, void* a)
        {
            *(Dual2f*)r = DualMath.fast_exp(*(Dual2f*)a);
        }

        public static void osl_exp_vv(void* r_, void* a_)
        {
            Vec3* r = (Vec3*)r_;
            Vec3* a = (Vec3*)a_;
            r->x = fmath.fast_exp(a->x);
            r->y = fmath.fast_exp(a->y);
            r->z = fmath.fast_exp(a->z);
        }

        public static void osl_exp_dvdv(void* r_, void* a_)
        {
            Dual2v3* r = (Dual2v3*)r_;
            Dual2v3* a = (Dual2v3*)a_;
            // Swizzle the Dual2<Vec3>'s into 3 Dual2<float>'s
            Dual2f ax = DualMath.fast_exp(new Dual2f(a->x.x, a->dx.x, a->dy.x));
            Dual2f ay = DualMath.fast_exp(new Dual2f(a->x.y, a->dx.y, a->dy.y));
            Dual2f az = DualMath.fast_exp(new Dual2f(a->x.z, a->dx.z, a->dy.z));
            // Now swizzle back
            r->set(new Vec3(ax.x, ay.x, az.x), new Vec3(ax.dx, ay.dx, az.dx), new Vec3(ax.dy, ay.dy, az.dy));
        }

        // MAKE_UNARY_PERCOMPONENT_OP(exp2)
        public static float osl_exp2_ff(float a)
        {
            return fmath.fast_exp2(a);
        }

        public static void osl_exp2_dfdf(void* r, void* a)
        {
            *(Dual2f*)r = DualMath.fast_exp2(*(Dual2f*)a);
        }

        public static void osl_exp2_vv(void* r_, void* a_)
        {
            Vec3* r = (Vec3*)r_;
            Vec3* a = (Vec3*)a_;
            r->x = fmath.fast_exp2(a->x);
            r->y = fmath.fast_exp2(a->y);
            r->z = fmath.fast_exp2(a->z);
        }

        public static void osl_exp2_dvdv(void* r_, void* a_)
        {
            Dual2v3* r = (Dual2v3*)r_;
            Dual2v3* a = (Dual2v3*)a_;
            // Swizzle the Dual2<Vec3>'s into 3 Dual2<float>'s
            Dual2f ax = DualMath.fast_exp2(new Dual2f(a->x.x, a->dx.x, a->dy.x));
            Dual2f ay = DualMath.fast_exp2(new Dual2f(a->x.y, a->dx.y, a->dy.y));
            Dual2f az = DualMath.fast_exp2(new Dual2f(a->x.z, a->dx.z, a->dy.z));
            // Now swizzle back
            r->set(new Vec3(ax.x, ay.x, az.x), new Vec3(ax.dx, ay.dx, az.dx), new Vec3(ax.dy, ay.dy, az.dy));
        }

        // MAKE_UNARY_PERCOMPONENT_OP(expm1)
        public static float osl_expm1_ff(float a)
        {
            return fmath.fast_expm1(a);
        }

        public static void osl_expm1_dfdf(void* r, void* a)
        {
            *(Dual2f*)r = DualMath.fast_expm1(*(Dual2f*)a);
        }

        public static void osl_expm1_vv(void* r_, void* a_)
        {
            Vec3* r = (Vec3*)r_;
            Vec3* a = (Vec3*)a_;
            r->x = fmath.fast_expm1(a->x);
            r->y = fmath.fast_expm1(a->y);
            r->z = fmath.fast_expm1(a->z);
        }

        public static void osl_expm1_dvdv(void* r_, void* a_)
        {
            Dual2v3* r = (Dual2v3*)r_;
            Dual2v3* a = (Dual2v3*)a_;
            // Swizzle the Dual2<Vec3>'s into 3 Dual2<float>'s
            Dual2f ax = DualMath.fast_expm1(new Dual2f(a->x.x, a->dx.x, a->dy.x));
            Dual2f ay = DualMath.fast_expm1(new Dual2f(a->x.y, a->dx.y, a->dy.y));
            Dual2f az = DualMath.fast_expm1(new Dual2f(a->x.z, a->dx.z, a->dy.z));
            // Now swizzle back
            r->set(new Vec3(ax.x, ay.x, az.x), new Vec3(ax.dx, ay.dx, az.dx), new Vec3(ax.dy, ay.dy, az.dy));
        }

        // MAKE_BINARY_PERCOMPONENT_OP(pow)
        public static float osl_pow_fff(float a, float b)
        {
            return fmath.fast_safe_pow(a, b);
        }

        public static void osl_pow_dfdfdf(void* r, void* a, void* b)
        {
            *(Dual2f*)r = DualMath.fast_safe_pow(*(Dual2f*)a, *(Dual2f*)b);
        }

        public static void osl_pow_dffdf(void* r, float a, void* b)
        {
            *(Dual2f*)r = DualMath.fast_safe_pow(new Dual2f(a), *(Dual2f*)b);
        }

        public static void osl_pow_dfdff(void* r, void* a, float b)
        {
            *(Dual2f*)r = DualMath.fast_safe_pow(*(Dual2f*)a, new Dual2f(b));
        }

        public static void osl_pow_vvv(void* r_, void* a_, void* b_)
        {
            Vec3* r = (Vec3*)r_;
            Vec3* a = (Vec3*)a_;
            Vec3* b = (Vec3*)b_;
            r->x = fmath.fast_safe_pow(a->x, b->x);
            r->y = fmath.fast_safe_pow(a->y, b->y);
            r->z = fmath.fast_safe_pow(a->z, b->z);
        }

        public static void osl_pow_dvdvdv(void* r_, void* a_, void* b_)
        {
            Dual2v3* r = (Dual2v3*)r_;
            Dual2v3* a = (Dual2v3*)a_;
            Dual2v3* b = (Dual2v3*)b_;
            // Swizzle the Dual2<Vec3>'s into 3 Dual2<float>'s
            Dual2f ax = DualMath.fast_safe_pow(new Dual2f(a->x.x, a->dx.x, a->dy.x), new Dual2f(b->x.x, b->dx.x, b->dy.x));
            Dual2f ay = DualMath.fast_safe_pow(new Dual2f(a->x.y, a->dx.y, a->dy.y), new Dual2f(b->x.y, b->dx.y, b->dy.y));
            Dual2f az = DualMath.fast_safe_pow(new Dual2f(a->x.z, a->dx.z, a->dy.z), new Dual2f(b->x.z, b->dx.z, b->dy.z));
            // Now swizzle back
            r->set(new Vec3(ax.x, ay.x, az.x), new Vec3(ax.dx, ay.dx, az.dx), new Vec3(ax.dy, ay.dy, az.dy));
        }

        public static void osl_pow_dvvdv(void* r_, void* a_, void* b_)
        {
            Dual2v3 a = new Dual2v3(*(Vec3*)a_);
            osl_pow_dvdvdv(r_, &a, b_);
        }

        public static void osl_pow_dvdvv(void* r_, void* a_, void* b_)
        {
            Dual2v3 b = new Dual2v3(*(Vec3*)b_);
            osl_pow_dvdvdv(r_, a_, &b);
        }

        // MAKE_BINARY_PERCOMPONENT_VF_OP(pow)
        public static void osl_pow_vvf(void* r_, void* a_, float b)
        {
            Vec3* r = (Vec3*)r_;
            Vec3* a = (Vec3*)a_;
            r->x = fmath.fast_safe_pow(a->x, b);
            r->y = fmath.fast_safe_pow(a->y, b);
            r->z = fmath.fast_safe_pow(a->z, b);
        }

        public static void osl_pow_dvdvdf(void* r_, void* a_, void* b_)
        {
            Dual2v3* r = (Dual2v3*)r_;
            Dual2v3* a = (Dual2v3*)a_;
            Dual2f b = *(Dual2f*)b_;
            Dual2f ax = DualMath.fast_safe_pow(new Dual2f(a->x.x, a->dx.x, a->dy.x), b);
            Dual2f ay = DualMath.fast_safe_pow(new Dual2f(a->x.y, a->dx.y, a->dy.y), b);
            Dual2f az = DualMath.fast_safe_pow(new Dual2f(a->x.z, a->dx.z, a->dy.z), b);
            // Now swizzle back
            r->set(new Vec3(ax.x, ay.x, az.x), new Vec3(ax.dx, ay.dx, az.dx), new Vec3(ax.dy, ay.dy, az.dy));
        }

        public static void osl_pow_dvvdf(void* r_, void* a_, void* b_)
        {
            Dual2v3 a = new Dual2v3(*(Vec3*)a_);
            osl_pow_dvdvdf(r_, &a, b_);
        }

        public static void osl_pow_dvdvf(void* r_, void* a_, float b_)
        {
            Dual2f b = new Dual2f(b_);
            osl_pow_dvdvdf(r_, a_, &b);
        }

        // MAKE_UNARY_PERCOMPONENT_OP(erf)
        public static float osl_erf_ff(float a)
        {
            return fmath.fast_erf(a);
        }

        public static void osl_erf_dfdf(void* r, void* a)
        {
            *(Dual2f*)r = DualMath.fast_erf(*(Dual2f*)a);
        }

        public static void osl_erf_vv(void* r_, void* a_)
        {
            Vec3* r = (Vec3*)r_;
            Vec3* a = (Vec3*)a_;
            r->x = fmath.fast_erf(a->x);
            r->y = fmath.fast_erf(a->y);
            r->z = fmath.fast_erf(a->z);
        }

        public static void osl_erf_dvdv(void* r_, void* a_)
        {
            Dual2v3* r = (Dual2v3*)r_;
            Dual2v3* a = (Dual2v3*)a_;
            // Swizzle the Dual2<Vec3>'s into 3 Dual2<float>'s
            Dual2f ax = DualMath.fast_erf(new Dual2f(a->x.x, a->dx.x, a->dy.x));
            Dual2f ay = DualMath.fast_erf(new Dual2f(a->x.y, a->dx.y, a->dy.y));
            Dual2f az = DualMath.fast_erf(new Dual2f(a->x.z, a->dx.z, a->dy.z));
            // Now swizzle back
            r->set(new Vec3(ax.x, ay.x, az.x), new Vec3(ax.dx, ay.dx, az.dx), new Vec3(ax.dy, ay.dy, az.dy));
        }

        // MAKE_UNARY_PERCOMPONENT_OP(erfc)
        public static float osl_erfc_ff(float a)
        {
            return fmath.fast_erfc(a);
        }

        public static void osl_erfc_dfdf(void* r, void* a)
        {
            *(Dual2f*)r = DualMath.fast_erfc(*(Dual2f*)a);
        }

        public static void osl_erfc_vv(void* r_, void* a_)
        {
            Vec3* r = (Vec3*)r_;
            Vec3* a = (Vec3*)a_;
            r->x = fmath.fast_erfc(a->x);
            r->y = fmath.fast_erfc(a->y);
            r->z = fmath.fast_erfc(a->z);
        }

        public static void osl_erfc_dvdv(void* r_, void* a_)
        {
            Dual2v3* r = (Dual2v3*)r_;
            Dual2v3* a = (Dual2v3*)a_;
            // Swizzle the Dual2<Vec3>'s into 3 Dual2<float>'s
            Dual2f ax = DualMath.fast_erfc(new Dual2f(a->x.x, a->dx.x, a->dy.x));
            Dual2f ay = DualMath.fast_erfc(new Dual2f(a->x.y, a->dx.y, a->dy.y));
            Dual2f az = DualMath.fast_erfc(new Dual2f(a->x.z, a->dx.z, a->dy.z));
            // Now swizzle back
            r->set(new Vec3(ax.x, ay.x, az.x), new Vec3(ax.dx, ay.dx, az.dx), new Vec3(ax.dy, ay.dy, az.dy));
        }

        // MAKE_UNARY_PERCOMPONENT_OP(cbrt)
        public static float osl_cbrt_ff(float a)
        {
            return fmath.fast_cbrt(a);
        }

        public static void osl_cbrt_dfdf(void* r, void* a)
        {
            *(Dual2f*)r = DualMath.fast_cbrt(*(Dual2f*)a);
        }

        public static void osl_cbrt_vv(void* r_, void* a_)
        {
            Vec3* r = (Vec3*)r_;
            Vec3* a = (Vec3*)a_;
            r->x = fmath.fast_cbrt(a->x);
            r->y = fmath.fast_cbrt(a->y);
            r->z = fmath.fast_cbrt(a->z);
        }

        public static void osl_cbrt_dvdv(void* r_, void* a_)
        {
            Dual2v3* r = (Dual2v3*)r_;
            Dual2v3* a = (Dual2v3*)a_;
            // Swizzle the Dual2<Vec3>'s into 3 Dual2<float>'s
            Dual2f ax = DualMath.fast_cbrt(new Dual2f(a->x.x, a->dx.x, a->dy.x));
            Dual2f ay = DualMath.fast_cbrt(new Dual2f(a->x.y, a->dx.y, a->dy.y));
            Dual2f az = DualMath.fast_cbrt(new Dual2f(a->x.z, a->dx.z, a->dy.z));
            // Now swizzle back
            r->set(new Vec3(ax.x, ay.x, az.x), new Vec3(ax.dx, ay.dx, az.dx), new Vec3(ax.dy, ay.dy, az.dy));
        }

        // MAKE_UNARY_PERCOMPONENT_OP(sqrt)
        public static float osl_sqrt_ff(float a)
        {
            return fmath.safe_sqrt(a);
        }

        public static void osl_sqrt_dfdf(void* r, void* a)
        {
            *(Dual2f*)r = DualMath.sqrt(*(Dual2f*)a);
        }

        public static void osl_sqrt_vv(void* r_, void* a_)
        {
            Vec3* r = (Vec3*)r_;
            Vec3* a = (Vec3*)a_;
            r->x = fmath.safe_sqrt(a->x);
            r->y = fmath.safe_sqrt(a->y);
            r->z = fmath.safe_sqrt(a->z);
        }

        public static void osl_sqrt_dvdv(void* r_, void* a_)
        {
            Dual2v3* r = (Dual2v3*)r_;
            Dual2v3* a = (Dual2v3*)a_;
            // Swizzle the Dual2<Vec3>'s into 3 Dual2<float>'s
            Dual2f ax = DualMath.sqrt(new Dual2f(a->x.x, a->dx.x, a->dy.x));
            Dual2f ay = DualMath.sqrt(new Dual2f(a->x.y, a->dx.y, a->dy.y));
            Dual2f az = DualMath.sqrt(new Dual2f(a->x.z, a->dx.z, a->dy.z));
            // Now swizzle back
            r->set(new Vec3(ax.x, ay.x, az.x), new Vec3(ax.dx, ay.dx, az.dx), new Vec3(ax.dy, ay.dy, az.dy));
        }

        // MAKE_UNARY_PERCOMPONENT_OP(inversesqrt)
        public static float osl_inversesqrt_ff(float a)
        {
            return fmath.safe_inversesqrt(a);
        }

        public static void osl_inversesqrt_dfdf(void* r, void* a)
        {
            *(Dual2f*)r = DualMath.inversesqrt(*(Dual2f*)a);
        }

        public static void osl_inversesqrt_vv(void* r_, void* a_)
        {
            Vec3* r = (Vec3*)r_;
            Vec3* a = (Vec3*)a_;
            r->x = fmath.safe_inversesqrt(a->x);
            r->y = fmath.safe_inversesqrt(a->y);
            r->z = fmath.safe_inversesqrt(a->z);
        }

        public static void osl_inversesqrt_dvdv(void* r_, void* a_)
        {
            Dual2v3* r = (Dual2v3*)r_;
            Dual2v3* a = (Dual2v3*)a_;
            // Swizzle the Dual2<Vec3>'s into 3 Dual2<float>'s
            Dual2f ax = DualMath.inversesqrt(new Dual2f(a->x.x, a->dx.x, a->dy.x));
            Dual2f ay = DualMath.inversesqrt(new Dual2f(a->x.y, a->dx.y, a->dy.y));
            Dual2f az = DualMath.inversesqrt(new Dual2f(a->x.z, a->dx.z, a->dy.z));
            // Now swizzle back
            r->set(new Vec3(ax.x, ay.x, az.x), new Vec3(ax.dx, ay.dx, az.dx), new Vec3(ax.dy, ay.dy, az.dy));
        }

        public static float osl_logb_ff(float x)
        {
            return fmath.fast_logb(x);
        }

        public static void osl_logb_vv(void* r, void* x_)
        {
            Vec3* x = (Vec3*)x_;
            *(Vec3*)r = new Vec3(fmath.fast_logb(x->x), fmath.fast_logb(x->y), fmath.fast_logb(x->z));
        }

        public static float osl_floor_ff(float x)
        {
            return MathF.Floor(x);
        }

        public static void osl_floor_vv(void* r, void* x_)
        {
            Vec3* x = (Vec3*)x_;
            *(Vec3*)r = new Vec3(MathF.Floor(x->x), MathF.Floor(x->y), MathF.Floor(x->z));
        }

        public static float osl_ceil_ff(float x)
        {
            return MathF.Ceiling(x);
        }

        public static void osl_ceil_vv(void* r, void* x_)
        {
            Vec3* x = (Vec3*)x_;
            *(Vec3*)r = new Vec3(MathF.Ceiling(x->x), MathF.Ceiling(x->y), MathF.Ceiling(x->z));
        }

        // roundf rounds halfway cases away from zero
        public static float osl_round_ff(float x)
        {
            return MathF.Round(x, MidpointRounding.AwayFromZero);
        }

        public static void osl_round_vv(void* r, void* x_)
        {
            Vec3* x = (Vec3*)x_;
            *(Vec3*)r = new Vec3(MathF.Round(x->x, MidpointRounding.AwayFromZero), MathF.Round(x->y, MidpointRounding.AwayFromZero), MathF.Round(x->z, MidpointRounding.AwayFromZero));
        }

        public static float osl_trunc_ff(float x)
        {
            return MathF.Truncate(x);
        }

        public static void osl_trunc_vv(void* r, void* x_)
        {
            Vec3* x = (Vec3*)x_;
            *(Vec3*)r = new Vec3(MathF.Truncate(x->x), MathF.Truncate(x->y), MathF.Truncate(x->z));
        }

        public static float osl_sign_ff(float x)
        {
            return x < 0.0f ? -1.0f : (x == 0.0f ? 0.0f : 1.0f);
        }

        public static void osl_sign_vv(void* r, void* x_)
        {
            Vec3* x = (Vec3*)x_;
            *(Vec3*)r = new Vec3(osl_sign_ff(x->x), osl_sign_ff(x->y), osl_sign_ff(x->z));
        }

        public static float osl_step_fff(float edge, float x)
        {
            return x < edge ? 0.0f : 1.0f;
        }

        public static void osl_step_vvv(void* result, void* edge, void* x)
        {
            *(Vec3*)result = new Vec3(((float*)x)[0] < ((float*)edge)[0] ? 0.0f : 1.0f,
                                      ((float*)x)[1] < ((float*)edge)[1] ? 0.0f : 1.0f,
                                      ((float*)x)[2] < ((float*)edge)[2] ? 0.0f : 1.0f);
        }

        public static int osl_isnan_if(float f)
        {
            return float.IsNaN(f) ? 1 : 0;
        }

        public static int osl_isinf_if(float f)
        {
            return float.IsInfinity(f) ? 1 : 0;
        }

        public static int osl_isfinite_if(float f)
        {
            return float.IsFinite(f) ? 1 : 0;
        }

        // abs(INT_MIN) is undefined in C; here it wraps
        public static int osl_abs_ii(int x)
        {
            return x < 0 ? unchecked(-x) : x;
        }

        public static int osl_fabs_ii(int x)
        {
            return x < 0 ? unchecked(-x) : x;
        }

        // MAKE_UNARY_PERCOMPONENT_OP(abs)
        public static float osl_abs_ff(float a)
        {
            return MathF.Abs(a);
        }

        public static void osl_abs_dfdf(void* r, void* a)
        {
            *(Dual2f*)r = DualMath.fabsf(*(Dual2f*)a);
        }

        public static void osl_abs_vv(void* r_, void* a_)
        {
            Vec3* r = (Vec3*)r_;
            Vec3* a = (Vec3*)a_;
            r->x = MathF.Abs(a->x);
            r->y = MathF.Abs(a->y);
            r->z = MathF.Abs(a->z);
        }

        public static void osl_abs_dvdv(void* r_, void* a_)
        {
            Dual2v3* r = (Dual2v3*)r_;
            Dual2v3* a = (Dual2v3*)a_;
            // Swizzle the Dual2<Vec3>'s into 3 Dual2<float>'s
            Dual2f ax = DualMath.fabsf(new Dual2f(a->x.x, a->dx.x, a->dy.x));
            Dual2f ay = DualMath.fabsf(new Dual2f(a->x.y, a->dx.y, a->dy.y));
            Dual2f az = DualMath.fabsf(new Dual2f(a->x.z, a->dx.z, a->dy.z));
            // Now swizzle back
            r->set(new Vec3(ax.x, ay.x, az.x), new Vec3(ax.dx, ay.dx, az.dx), new Vec3(ax.dy, ay.dy, az.dy));
        }

        // MAKE_UNARY_PERCOMPONENT_OP(fabs)
        public static float osl_fabs_ff(float a)
        {
            return MathF.Abs(a);
        }

        public static void osl_fabs_dfdf(void* r, void* a)
        {
            *(Dual2f*)r = DualMath.fabsf(*(Dual2f*)a);
        }

        public static void osl_fabs_vv(void* r_, void* a_)
        {
            Vec3* r = (Vec3*)r_;
            Vec3* a = (Vec3*)a_;
            r->x = MathF.Abs(a->x);
            r->y = MathF.Abs(a->y);
            r->z = MathF.Abs(a->z);
        }

        public static void osl_fabs_dvdv(void* r_, void* a_)
        {
            Dual2v3* r = (Dual2v3*)r_;
            Dual2v3* a = (Dual2v3*)a_;
            // Swizzle the Dual2<Vec3>'s into 3 Dual2<float>'s
            Dual2f ax = DualMath.fabsf(new Dual2f(a->x.x, a->dx.x, a->dy.x));
            Dual2f ay = DualMath.fabsf(new Dual2f(a->x.y, a->dx.y, a->dy.y));
            Dual2f az = DualMath.fabsf(new Dual2f(a->x.z, a->dx.z, a->dy.z));
            // Now swizzle back
            r->set(new Vec3(ax.x, ay.x, az.x), new Vec3(ax.dx, ay.dx, az.dx), new Vec3(ax.dy, ay.dy, az.dy));
        }

        // C: (b != 0) ? (a % b) : 0.  INT_MIN % -1 traps in C and throws in .NET; here it is 0, which is the mathematically right answer.
        public static int osl_safe_mod_iii(int a, int b)
        {
            if (b == -1)
            {
                return 0;
            }
            return (b != 0) ? (a % b) : 0;
        }

        // MAKE_BINARY_PERCOMPONENT_OP(fmod)
        public static float osl_fmod_fff(float a, float b)
        {
            return fmath.safe_fmod(a, b);
        }

        public static void osl_fmod_dfdfdf(void* r, void* a, void* b)
        {
            *(Dual2f*)r = DualMath.safe_fmod(*(Dual2f*)a, *(Dual2f*)b);
        }

        public static void osl_fmod_dffdf(void* r, float a, void* b)
        {
            *(Dual2f*)r = DualMath.safe_fmod(new Dual2f(a), *(Dual2f*)b);
        }

        public static void osl_fmod_dfdff(void* r, void* a, float b)
        {
            *(Dual2f*)r = DualMath.safe_fmod(*(Dual2f*)a, new Dual2f(b));
        }

        public static void osl_fmod_vvv(void* r_, void* a_, void* b_)
        {
            Vec3* r = (Vec3*)r_;
            Vec3* a = (Vec3*)a_;
            Vec3* b = (Vec3*)b_;
            r->x = fmath.safe_fmod(a->x, b->x);
            r->y = fmath.safe_fmod(a->y, b->y);
            r->z = fmath.safe_fmod(a->z, b->z);
        }

        public static void osl_fmod_dvdvdv(void* r_, void* a_, void* b_)
        {
            Dual2v3* r = (Dual2v3*)r_;
            Dual2v3* a = (Dual2v3*)a_;
            Dual2v3* b = (Dual2v3*)b_;
            // Swizzle the Dual2<Vec3>'s into 3 Dual2<float>'s
            Dual2f ax = DualMath.safe_fmod(new Dual2f(a->x.x, a->dx.x, a->dy.x), new Dual2f(b->x.x, b->dx.x, b->dy.x));
            Dual2f ay = DualMath.safe_fmod(new Dual2f(a->x.y, a->dx.y, a->dy.y), new Dual2f(b->x.y, b->dx.y, b->dy.y));
            Dual2f az = DualMath.safe_fmod(new Dual2f(a->x.z, a->dx.z, a->dy.z), new Dual2f(b->x.z, b->dx.z, b->dy.z));
            // Now swizzle back
            r->set(new Vec3(ax.x, ay.x, az.x), new Vec3(ax.dx, ay.dx, az.dx), new Vec3(ax.dy, ay.dy, az.dy));
        }

        public static void osl_fmod_dvvdv(void* r_, void* a_, void* b_)
        {
            Dual2v3 a = new Dual2v3(*(Vec3*)a_);
            osl_fmod_dvdvdv(r_, &a, b_);
        }

        public static void osl_fmod_dvdvv(void* r_, void* a_, void* b_)
        {
            Dual2v3 b = new Dual2v3(*(Vec3*)b_);
            osl_fmod_dvdvdv(r_, a_, &b);
        }

        // MAKE_BINARY_PERCOMPONENT_VF_OP(fmod)
        public static void osl_fmod_vvf(void* r_, void* a_, float b)
        {
            Vec3* r = (Vec3*)r_;
            Vec3* a = (Vec3*)a_;
            r->x = fmath.safe_fmod(a->x, b);
            r->y = fmath.safe_fmod(a->y, b);
            r->z = fmath.safe_fmod(a->z, b);
        }

        public static void osl_fmod_dvdvdf(void* r_, void* a_, void* b_)
        {
            Dual2v3* r = (Dual2v3*)r_;
            Dual2v3* a = (Dual2v3*)a_;
            Dual2f b = *(Dual2f*)b_;
            Dual2f ax = DualMath.safe_fmod(new Dual2f(a->x.x, a->dx.x, a->dy.x), b);
            Dual2f ay = DualMath.safe_fmod(new Dual2f(a->x.y, a->dx.y, a->dy.y), b);
            Dual2f az = DualMath.safe_fmod(new Dual2f(a->x.z, a->dx.z, a->dy.z), b);
            // Now swizzle back
            r->set(new Vec3(ax.x, ay.x, az.x), new Vec3(ax.dx, ay.dx, az.dx), new Vec3(ax.dy, ay.dy, az.dy));
        }

        public static void osl_fmod_dvvdf(void* r_, void* a_, void* b_)
        {
            Dual2v3 a = new Dual2v3(*(Vec3*)a_);
            osl_fmod_dvdvdf(r_, &a, b_);
        }

        public static void osl_fmod_dvdvf(void* r_, void* a_, float b_)
        {
            Dual2f b = new Dual2f(b_);
            osl_fmod_dvdvdf(r_, a_, &b);
        }

        private static float safe_div(float a, float b)
        {
            // Checking for b == 0.0f is not good enough to avoid +INF or -INF that can be produced by small near 0 values like b == 1.0e-40;
            // check the result of the divide to see if is finite and return 0.0f when it is not.
            float q = a / b;
            return float.IsFinite(q) ? q : 0.0f;
        }

        public static float osl_safe_div_fff(float a, float b)
        {
            return safe_div(a, b);
        }

        // C: (b != 0) ? (a / b) : 0.  INT_MIN / -1 traps in C and throws in .NET; here it wraps to INT_MIN.
        public static int osl_safe_div_iii(int a, int b)
        {
            if (b == -1)
            {
                return unchecked(-a);
            }
            return (b != 0) ? (a / b) : 0;
        }

        public static float osl_smoothstep_ffff(float e0, float e1, float x)
        {
            return DualMath.smoothstep(e0, e1, x);
        }

        public static void osl_smoothstep_dfffdf(void* result, float e0_, float e1_, void* x_)
        {
            Dual2f e0 = new Dual2f(e0_);
            Dual2f e1 = new Dual2f(e1_);
            Dual2f x = *(Dual2f*)x_;

            *(Dual2f*)result = DualMath.smoothstep(e0, e1, x);
        }

        public static void osl_smoothstep_dffdff(void* result, float e0_, void* e1_, float x_)
        {
            Dual2f e0 = new Dual2f(e0_);
            Dual2f e1 = *(Dual2f*)e1_;
            Dual2f x = new Dual2f(x_);

            *(Dual2f*)result = DualMath.smoothstep(e0, e1, x);
        }

        public static void osl_smoothstep_dffdfdf(void* result, float e0_, void* e1_, void* x_)
        {
            Dual2f e0 = new Dual2f(e0_);
            Dual2f e1 = *(Dual2f*)e1_;
            Dual2f x = *(Dual2f*)x_;

            *(Dual2f*)result = DualMath.smoothstep(e0, e1, x);
        }

        public static void osl_smoothstep_dfdfff(void* result, void* e0_, float e1_, float x_)
        {
            Dual2f e0 = *(Dual2f*)e0_;
            Dual2f e1 = new Dual2f(e1_);
            Dual2f x = new Dual2f(x_);

            *(Dual2f*)result = DualMath.smoothstep(e0, e1, x);
        }

        public static void osl_smoothstep_dfdffdf(void* result, void* e0_, float e1_, void* x_)
        {
            Dual2f e0 = *(Dual2f*)e0_;
            Dual2f e1 = new Dual2f(e1_);
            Dual2f x = *(Dual2f*)x_;

            *(Dual2f*)result = DualMath.smoothstep(e0, e1, x);
        }

        public static void osl_smoothstep_dfdfdff(void* result, void* e0_, void* e1_, float x_)
        {
            Dual2f e0 = *(Dual2f*)e0_;
            Dual2f e1 = *(Dual2f*)e1_;
            Dual2f x = new Dual2f(x_);

            *(Dual2f*)result = DualMath.smoothstep(e0, e1, x);
        }

        public static void osl_smoothstep_dfdfdfdf(void* result, void* e0_, void* e1_, void* x_)
        {
            Dual2f e0 = *(Dual2f*)e0_;
            Dual2f e1 = *(Dual2f*)e1_;
            Dual2f x = *(Dual2f*)x_;

            *(Dual2f*)result = DualMath.smoothstep(e0, e1, x);
        }

        // Vector ops

        public static float osl_dot_fvv(void* a, void* b)
        {
            Vec3* va = (Vec3*)a;
            Vec3* vb = (Vec3*)b;
            return va->x * vb->x + va->y * vb->y + va->z * vb->z;
        }

        public static void osl_dot_dfdvdv(void* result, void* a, void* b)
        {
            *(Dual2f*)result = DualMath.dot(*(Dual2v3*)a, *(Dual2v3*)b);
        }

        public static void osl_dot_dfdvv(void* result, void* a, void* b_)
        {
            Dual2v3 b = new Dual2v3(*(Vec3*)b_);
            osl_dot_dfdvdv(result, a, &b);
        }

        public static void osl_dot_dfvdv(void* result, void* a_, void* b)
        {
            Dual2v3 a = new Dual2v3(*(Vec3*)a_);
            osl_dot_dfdvdv(result, &a, b);
        }

        public static void osl_cross_vvv(void* result, void* a, void* b)
        {
            Vec3 va = *(Vec3*)a;
            Vec3 vb = *(Vec3*)b;
            *(Vec3*)result = va.cross(vb);
        }

        public static void osl_cross_dvdvdv(void* result, void* a, void* b)
        {
            *(Dual2v3*)result = DualMath.cross(*(Dual2v3*)a, *(Dual2v3*)b);
        }

        public static void osl_cross_dvdvv(void* result, void* a, void* b_)
        {
            Dual2v3 b = new Dual2v3(*(Vec3*)b_);
            osl_cross_dvdvdv(result, a, &b);
        }

        public static void osl_cross_dvvdv(void* result, void* a_, void* b)
        {
            Dual2v3 a = new Dual2v3(*(Vec3*)a_);
            osl_cross_dvdvdv(result, &a, b);
        }

        // Imath::Vec3<float>::length(), including its handling of lengths whose square would underflow (lengthTiny)
        private static float Vec3Length(Vec3 v)
        {
            float length2 = v.x * v.x + v.y * v.y + v.z * v.z;
            if (length2 < 2.0f * fmath.float_min)
            {
                float absX = (v.x >= 0.0f) ? v.x : -v.x;
                float absY = (v.y >= 0.0f) ? v.y : -v.y;
                float absZ = (v.z >= 0.0f) ? v.z : -v.z;
                float max = absX;
                if (max < absY)
                {
                    max = absY;
                }
                if (max < absZ)
                {
                    max = absZ;
                }
                if (max == 0.0f)
                {
                    return 0.0f;
                }
                absX /= max;
                absY /= max;
                absZ /= max;
                return max * MathF.Sqrt(absX * absX + absY * absY + absZ * absZ);
            }
            return MathF.Sqrt(length2);
        }

        public static float osl_length_fv(void* a)
        {
            return Vec3Length(*(Vec3*)a);
        }

        public static void osl_length_dfdv(void* result, void* a)
        {
            *(Dual2f*)result = DualMath.length(*(Dual2v3*)a);
        }

        public static float osl_distance_fvv(void* a_, void* b_)
        {
            Vec3* a = (Vec3*)a_;
            Vec3* b = (Vec3*)b_;
            float x = a->x - b->x;
            float y = a->y - b->y;
            float z = a->z - b->z;
            return MathF.Sqrt(x * x + y * y + z * z);
        }

        public static void osl_distance_dfdvdv(void* result, void* a, void* b)
        {
            *(Dual2f*)result = DualMath.distance(*(Dual2v3*)a, *(Dual2v3*)b);
        }

        public static void osl_distance_dfdvv(void* result, void* a, void* b)
        {
            *(Dual2f*)result = DualMath.distance(*(Dual2v3*)a, *(Vec3*)b);
        }

        public static void osl_distance_dfvdv(void* result, void* a, void* b)
        {
            *(Dual2f*)result = DualMath.distance(*(Vec3*)a, *(Dual2v3*)b);
        }

        public static void osl_normalize_vv(void* result, void* a)
        {
            // NOTE: must match with the Dual version of normalize
            Vec3 v = *(Vec3*)a;
            float len = MathF.Sqrt(v.x * v.x + v.y * v.y + v.z * v.z);
            if (len > 0.0f)
            {
                float invlen = 1.0f / len;
                v.x *= invlen;
                v.y *= invlen;
                v.z *= invlen;
            }
            else
            {
                v.x = v.y = v.z = 0.0f;
            }
            *(Vec3*)result = v;
        }

        public static void osl_normalize_dvdv(void* result, void* a)
        {
            *(Dual2v3*)result = DualMath.normalize(*(Dual2v3*)a);
        }

        // osl_calculatenormal needs the ShadingContext (flipHandedness) and is not ported; area and filterwidth are pure.
        private static Vec3 calculatenormal(void* P_)
        {
            Dual2v3* tmpP = (Dual2v3*)P_;
            return tmpP->dx.cross(tmpP->dy);
        }

        public static float osl_area(void* P_)
        {
            Vec3 N = calculatenormal(P_);
            return Vec3Length(N);
        }

        private static float filter_width(float dx, float dy)
        {
            return MathF.Sqrt(dx * dx + dy * dy);
        }

        public static float osl_filterwidth_fdf(void* x_)
        {
            Dual2f* x = (Dual2f*)x_;
            return filter_width(x->dx, x->dy);
        }

        public static void osl_filterwidth_vdv(void* out_, void* x_)
        {
            Dual2v3* x = (Dual2v3*)x_;
            Vec3* o = (Vec3*)out_;
            o->x = filter_width(x->dx.x, x->dy.x);
            o->y = filter_width(x->dx.y, x->dy.y);
            o->z = filter_width(x->dx.z, x->dy.z);
        }
    }
}
