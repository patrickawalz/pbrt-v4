// OSL port: testrender/background.h
//
// The importance sampled environment: the background shader is evaluated over a lat-long table and turned into a two level CDF (rows, then the columns of
// a row), so that the path tracer can pick directions towards the bright parts of the background.

using System;
using OIIO;

namespace OSL
{
    public sealed class Background
    {
        private Vec3[] values;  // actual map
        private float[] rows;   // probability of choosing a given row 'y'
        private float[] cols;   // probability of choosing a given column 'x', given that we've chosen row 'y'
        private int res = -1;   // resolution in pixels of the precomputed table
        private float invres;   // 1 / resolution
        private float invjacobian;

        /// The background shader evaluated for a direction and its footprint
        public delegate Vec3 Evaluator(DualVec3 dir);

        /// std::upper_bound on data[offset, offset + count): the first index whose value is greater than the given value
        private static int upper_bound(float[] data, int offset, int count, float value)
        {
            int first = offset;
            int len = count;
            while (len != 0)
            {
                int l2 = len / 2;
                int m = first + l2;
                if (value < data[m])
                {
                    len = l2;
                }
                else
                {
                    first = m + 1;
                    len -= l2 + 1;
                }
            }
            return first;
        }

        public void prepare(int resolution, Evaluator cb)
        {
            res = resolution;
            if (res < 32)
            {
                res = 32;  // validate
            }
            invres = 1.0f / res;
            invjacobian = res * res / (float)(4 * Math.PI);
            values = new Vec3[res * res];
            rows = new float[res];
            cols = new float[res * res];

            int i = 0;
            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++, i++)
                {
                    values[i] = cb(map(x + 0.5f, y + 0.5f));
                    cols[i] = MathF.Max(MathF.Max(values[i].x, values[i].y), values[i].z) + ((x > 0) ? cols[i - 1] : 0.0f);
                }
                rows[y] = cols[i - 1] + ((y > 0) ? rows[y - 1] : 0.0f);
                // normalize the pdf for this scanline (if it was non-zero)
                if (cols[i - 1] > 0)
                {
                    for (int x = 0; x < res; x++)
                    {
                        cols[i - res + x] /= cols[i - 1];
                    }
                }
            }
            // normalize the pdf across all scanlines
            for (int y = 0; y < res; y++)
            {
                rows[y] /= rows[res - 1];
            }

            // both eval and sample below return a "weight" that is value[i] / row*col_pdf, so might as well bake it into the table
            i = 0;
            for (int y = 0; y < res; y++)
            {
                float row_pdf = rows[y] - (y > 0 ? rows[y - 1] : 0.0f);
                for (int x = 0; x < res; x++, i++)
                {
                    float col_pdf = cols[i] - (x > 0 ? cols[i - 1] : 0.0f);
                    float d = row_pdf * col_pdf * invjacobian;
                    values[i] = new Vec3(values[i].x / d, values[i].y / d, values[i].z / d);
                }
            }
        }

        public Vec3 eval(Vec3 dir, out float pdf)
        {
            // map from sphere to unit-square
            float u = fmath.fast_atan2(dir.y, dir.x) * (float)(0.318309886183790671538 * 0.5f);
            if (u < 0)
            {
                u++;
            }
            float v = (1 - dir.z) * 0.5f;
            // retrieve nearest neighbor
            int x = (int)(u * res);
            if (x < 0)
            {
                x = 0;
            }
            else if (x >= res)
            {
                x = res - 1;
            }
            int y = (int)(v * res);
            if (y < 0)
            {
                y = 0;
            }
            else if (y >= res)
            {
                y = res - 1;
            }
            int i = y * res + x;
            float row_pdf = rows[y] - (y > 0 ? rows[y - 1] : 0.0f);
            float col_pdf = cols[i] - (x > 0 ? cols[i - 1] : 0.0f);
            pdf = MathF.Max(0.0f, row_pdf * col_pdf * invjacobian);
            return values[i];
        }

        public Vec3 sample(float rx, float ry, out DualVec3 dir, out float pdf)
        {
            float row_pdf;
            float col_pdf;
            int x;
            int y;
            ry = sample_cdf(rows, 0, res, ry, out y, out row_pdf);
            rx = sample_cdf(cols, y * res, res, rx, out x, out col_pdf);
            dir = map(x + rx, y + ry);
            pdf = MathF.Max(0.0f, row_pdf * col_pdf * invjacobian);
            return values[y * res + x];
        }

        private struct Dual
        {
            public float val, dx, dy;

            public Dual(float v, float dx, float dy)
            {
                val = v;
                this.dx = dx;
                this.dy = dy;
            }

            public static Dual operator *(Dual a, Dual b)
            {
                return new Dual(a.val * b.val, a.val * b.dx + a.dx * b.val, a.val * b.dy + a.dy * b.val);
            }

            public static Dual operator *(Dual a, float b)
            {
                return new Dual(a.val * b, a.dx * b, a.dy * b);
            }

            public static Dual operator *(float a, Dual b)
            {
                return new Dual(b.val * a, b.dx * a, b.dy * a);
            }

            public static Dual operator -(float a, Dual b)
            {
                return new Dual(a - b.val, -b.dx, -b.dy);
            }
        }

        private static Dual dualfunc(Dual u, float f_val, float df_val)
        {
            return new Dual(f_val, df_val * u.dx, df_val * u.dy);
        }

        private static Dual dual_sqrt(Dual a)
        {
            if (a.val > 0)
            {
                float f = MathF.Sqrt(a.val);
                float df = 0.5f / f;
                return dualfunc(a, f, df);
            }
            return new Dual(0, 0, 0);
        }

        private unsafe DualVec3 map(float x, float y)
        {
            // pixel coordinates of entry (x,y)
            Dual u = new Dual(x, 1, 0) * invres;
            Dual v = new Dual(y, 0, 1) * invres;
            Dual theta = u * (float)(2 * Math.PI);
            float sina;
            float cosa;
            fmath.fast_sincos(theta.val, &sina, &cosa);
            Dual ct = dualfunc(theta, cosa, -sina);
            Dual st = dualfunc(theta, sina, cosa);
            Dual cos_phi = 1.0f - 2.0f * v;
            Dual sin_phi = dual_sqrt(1.0f - cos_phi * cos_phi);
            Dual px = sin_phi * ct;
            Dual py = sin_phi * st;
            DualVec3 r;
            r.val = new Vec3(px.val, py.val, cos_phi.val);
            r.dx = new Vec3(px.dx, py.dx, cos_phi.dx);
            r.dy = new Vec3(px.dy, py.dy, cos_phi.dy);
            return r;
        }

        private static float sample_cdf(float[] data, int offset, int n, float x, out int idx, out float pdf)
        {
            idx = upper_bound(data, offset, n, x) - offset;
            float scaled_sample;
            if (idx == 0)
            {
                pdf = data[offset];
                scaled_sample = x / data[offset];
            }
            else
            {
                pdf = data[offset + idx] - data[offset + idx - 1];
                scaled_sample = (x - data[offset + idx - 1]) / (data[offset + idx] - data[offset + idx - 1]);
            }
            // keep result in [0,1)
            return MathF.Min(scaled_sample, 0.99999994f);
        }
    }
}
