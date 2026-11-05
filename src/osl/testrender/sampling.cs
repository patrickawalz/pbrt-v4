// OSL port: testrender/sampling.h
//
// Sampling tools of the test renderer: the tangent frame, hemisphere and disk mappings, the multiple importance sampling helpers, and the low
// discrepancy sampler (a Sobol sequence with Owen scrambling, so every pixel and path gets a repeatable sequence).

using System;
using OIIO;

namespace OSL
{
    /// Float helpers on Vec3 with the semantics of Imath::Vec3<float>
    public static class V3
    {
        public static Vec3 normalize(Vec3 v)
        {
            float l = MathF.Sqrt(v.dot(v));
            if (l == 0.0f)
            {
                return v;
            }
            return new Vec3(v.x / l, v.y / l, v.z / l);
        }

        public static Vec3 normalized(Vec3 v)
        {
            float l = MathF.Sqrt(v.dot(v));
            if (l == 0.0f)
            {
                return new Vec3(0.0f, 0.0f, 0.0f);
            }
            return new Vec3(v.x / l, v.y / l, v.z / l);
        }
    }

    public struct Vec2f
    {
        public float x, y;

        public Vec2f(float x, float y)
        {
            this.x = x;
            this.y = y;
        }

        public static Vec2f operator +(Vec2f a, Vec2f b) { return new Vec2f(a.x + b.x, a.y + b.y); }
        public static Vec2f operator -(Vec2f a, Vec2f b) { return new Vec2f(a.x - b.x, a.y - b.y); }
        public static Vec2f operator *(Vec2f a, float s) { return new Vec2f(a.x * s, a.y * s); }
        public static Vec2f operator *(float s, Vec2f a) { return new Vec2f(a.x * s, a.y * s); }
        public static Vec2f operator *(Vec2f a, Vec2f b) { return new Vec2f(a.x * b.x, a.y * b.y); }
    }

    public struct TangentFrame
    {
        public Vec3 u, v, w;

        public static TangentFrame from_normal(Vec3 n)
        {
            float sign = MathF.CopySign(1.0f, n.z);
            float a = -1 / (sign + n.z);
            float b = n.x * n.y * a;
            TangentFrame f;
            f.u = new Vec3(1 + sign * n.x * n.x * a, sign * b, -sign * n.x);
            f.v = new Vec3(b, sign + n.y * n.y * a, -n.y);
            f.w = n;
            return f;
        }

        public static TangentFrame from_normal_and_tangent(Vec3 n, Vec3 t)
        {
            Vec3 x = t - n * n.dot(t);
            float xlen2 = x.dot(x);
            if (xlen2 > 0)
            {
                x *= 1.0f / MathF.Sqrt(xlen2);
                TangentFrame f;
                f.u = x;
                f.v = n.cross(x);
                f.w = n;
                return f;
            }
            return from_normal(n);
        }

        public Vec3 get(float x, float y, float z)
        {
            return x * u + y * v + z * w;
        }

        public float getx(Vec3 a) { return a.dot(u); }
        public float gety(Vec3 a) { return a.dot(v); }
        public float getz(Vec3 a) { return a.dot(w); }

        public Vec3 tolocal(Vec3 a)
        {
            return new Vec3(a.dot(u), a.dot(v), a.dot(w));
        }

        public Vec3 toworld(Vec3 a)
        {
            return get(a.x, a.y, a.z);
        }
    }

    public static unsafe class Sampling
    {
        public static void to_unit_disk(ref float x, ref float y)
        {
            const float PI_OVER_4 = (float)(Math.PI / 4);
            const float PI_OVER_2 = (float)(Math.PI / 2);
            float phi;
            float r;
            float a = 2 * x - 1;
            float b = 2 * y - 1;
            if (a * a > b * b)
            {
                r = a;
                phi = PI_OVER_4 * (b / a);
            }
            else if (b != 0)
            {
                r = b;
                phi = PI_OVER_2 - PI_OVER_4 * (a / b);
            }
            else
            {
                r = 0;
                phi = 0;
            }
            float s;
            float c;
            fmath.fast_sincos(phi, &s, &c);
            x = s;
            y = c;
            x *= r;
            y *= r;
        }

        public static void sample_cosine_hemisphere(Vec3 N, float rndx, float rndy, out Vec3 outv, out float pdf)
        {
            to_unit_disk(ref rndx, ref rndy);
            float cos_theta = MathF.Sqrt(MathF.Max(1 - rndx * rndx - rndy * rndy, 0.0f));
            outv = TangentFrame.from_normal(N).get(rndx, rndy, cos_theta);
            pdf = cos_theta * (float)(1.0 / Math.PI);
        }

        public static void sample_uniform_hemisphere(Vec3 N, float rndx, float rndy, out Vec3 outv, out float pdf)
        {
            float phi = (float)(2 * Math.PI) * rndx;
            float cos_theta = rndy;
            float sin_theta = MathF.Sqrt(1 - cos_theta * cos_theta);
            outv = TangentFrame.from_normal(N).get(sin_theta * MathF.Cos(phi), sin_theta * MathF.Sin(phi), cos_theta);
            pdf = (float)(0.5 * (1.0 / Math.PI));
        }
    }

    public static class MIS
    {
        public enum MISMode
        {
            WEIGHT_WEIGHT, WEIGHT_EVAL, EVAL_WEIGHT
        }

        public static float power_heuristic(MISMode mode, float sampled_pdf, float other_pdf)
        {
            float r;
            float mis;
            if (sampled_pdf > other_pdf)
            {
                r = other_pdf / sampled_pdf;
                mis = 1 / (1 + r * r);
            }
            else if (sampled_pdf < other_pdf)
            {
                r = sampled_pdf / other_pdf;
                mis = 1 - 1 / (1 + r * r);
            }
            else
            {
                r = 1.0f;
                mis = 0.5f;
            }
            const float MAX = float.MaxValue;
            switch (mode)
            {
                case MISMode.WEIGHT_WEIGHT:
                    return MathF.Min(other_pdf, MAX) * mis;
                case MISMode.WEIGHT_EVAL:
                    return mis;
                case MISMode.EVAL_WEIGHT:
                    return mis * ((other_pdf > sampled_pdf) ? MathF.Min(1 / r, MAX) : r);
            }
            return 0;
        }

        public static void update_eval(ref Color3 w, ref float pdf, Color3 ow, float opdf, float b)
        {
            if (b > 1.17549435e-38f)
            {
                opdf *= b;
                ow *= 1 / b;
                float mis;
                if (pdf < opdf)
                {
                    mis = 1 / (1 + pdf / opdf);
                }
                else if (opdf < pdf)
                {
                    mis = 1 - 1 / (1 + opdf / pdf);
                }
                else
                {
                    mis = 0.5f;
                }
                w = w * (1 - mis) + ow * mis;
                pdf += opdf;
            }
        }
    }

    public sealed class Sampler
    {
        private static readonly uint[] zmatrix =
        {
            0x000001u, 0x000003u, 0x000006u, 0x000009u, 0x000017u, 0x00003au, 0x000071u, 0x0000a3u, 0x000116u, 0x000339u, 0x000677u, 0x0009aau,
            0x001601u, 0x003903u, 0x007706u, 0x00aa09u, 0x010117u, 0x03033au, 0x060671u, 0x0909a3u, 0x171616u, 0x3a3939u, 0x717777u, 0xa3aaaau
        };

        private uint seed;
        private uint index;

        public Sampler(int px, int py, int si)
        {
            seed = (uint)(((px & 2047) << 22) | ((py & 2047) << 11));
            index = reversebits((uint)si);
        }

        public Vec3 get()
        {
            seed += 4;
            uint scrambled_index = owen_scramble(index, hash(seed - 4)) & 0xFFFFFF;
            uint result_x = scrambled_index;
            uint result_y = 0;
            uint result_z = 0;
            uint ymatrix = 1;
            for (int c = 0; c < 24; c++)
            {
                uint bit = (scrambled_index >> c) & 1;
                result_y ^= bit * ymatrix;
                result_z ^= bit * zmatrix[c];
                ymatrix ^= ymatrix << 1;
            }
            return new Vec3((owen_scramble(result_x, hash(seed - 3)) >> 8) * 5.96046448e-8f, (owen_scramble(result_y, hash(seed - 2)) >> 8) * 5.96046448e-8f,
                            (owen_scramble(result_z, hash(seed - 1)) >> 8) * 5.96046448e-8f);
        }

        private static uint hash(uint s)
        {
            s ^= s >> 16;
            s *= 0x21f0aaadu;
            s ^= s >> 15;
            s *= 0xd35a2d97u;
            s ^= s >> 15;
            return s;
        }

        private static uint reversebits(uint x)
        {
            x = (x << 16) | (x >> 16);
            x = ((x & 0x00ff00ffu) << 8) | ((x & 0xff00ff00u) >> 8);
            x = ((x & 0x0f0f0f0fu) << 4) | ((x & 0xf0f0f0f0u) >> 4);
            x = ((x & 0x33333333u) << 2) | ((x & 0xccccccccu) >> 2);
            x = ((x & 0x55555555u) << 1) | ((x & 0xaaaaaaaau) >> 1);
            return x;
        }

        private static uint owen_scramble(uint p, uint s)
        {
            p ^= p * 0x3d20adeau;
            p += s;
            p *= (s >> 16) | 1;
            p ^= p * 0x05526c56u;
            p ^= p * 0x53a22864u;
            return reversebits(p);
        }
    }
}

namespace OSL
{
    /// The formatting forms of OIIO::ErrorHandler (errorfmt("... {} ...", args))
    public static class ErrorHandlerFormat
    {
        public static void errorfmt(this ErrorHandler eh, string fmt, params object[] args) { eh.errorfmt(Fmt.format(fmt, args)); }
        public static void warningfmt(this ErrorHandler eh, string fmt, params object[] args) { eh.warningfmt(Fmt.format(fmt, args)); }
        public static void infofmt(this ErrorHandler eh, string fmt, params object[] args) { eh.infofmt(Fmt.format(fmt, args)); }
        public static void severefmt(this ErrorHandler eh, string fmt, params object[] args) { eh.severefmt(Fmt.format(fmt, args)); }
    }
}
