// PtexTriangleKernel.h, PtexTriangleKernel.cpp
/*
PTEX SOFTWARE
Copyright 2014 Disney Enterprises, Inc.  All rights reserved
(see LICENSE.txt in ext/ptex)
*/

using System;

namespace Ptex
{
    public static unsafe partial class PtexGlobals
    {
        // kernel width as a multiple of filter width (should be between 3 and 4)
        // for values below 3, the gaussian is not close to zero and a contour will be formed
        // larger values are more expensive (proportional to width-squared)
        public const float PtexTriangleKernelWidth = 3.5f;
    }

    // Triangle filter kernel iterator (in texel coords)
    public unsafe struct PtexTriangleKernelIter
    {
        public int rowlen;                 // row length (in u)
        public float u, v;                 // uv center in texels
        public int u1, v1, w1;             // uvw lower bounds
        public int u2, v2, w2;             // uvw upper bounds
        public float A, B, C;              // ellipse coefficients (F = 1)
        public bool valid;                 // footprint is valid (non-empty)
        public float wscale;               // amount to scale weights by (proportional to texel area)
        public float weight;               // accumulated weight

        public void apply(float* dst, void* data, DataType dt, int nChan, int nTxChan)
        {
            // (the unrolled per-channel-count variants of the C++ are numerically identical)
            switch (dt)
            {
                case DataType.dt_uint8: Apply<byte, PixU8>(dst, data, nChan, nTxChan); break;
                case DataType.dt_uint16: Apply<ushort, PixU16>(dst, data, nChan, nTxChan); break;
                case DataType.dt_half: Apply<PtexHalf, PixHalf>(dst, data, nChan, nTxChan); break;
                default: Apply<float, PixFloat>(dst, data, nChan, nTxChan); break;
            }
        }

        static float gaussian(float x_squared)
        {
            const float scale = -0.5f * (PtexGlobals.PtexTriangleKernelWidth * PtexGlobals.PtexTriangleKernelWidth);
            return (float)MathF.Exp(scale * x_squared);
        }

        // the ellipse equation, Q, is calculated via finite differences (Heckbert '89 pg 57)
        void Apply<T, TR>(float* result, void* data, int nChan, int nTxChan) where T : unmanaged where TR : struct, IPtexPixel<T>
        {
            float DDQ = 2.0f * A;
            for (int vi = v1; vi != v2; vi++)
            {
                int xw = rowlen - vi;
                int x1 = PtexUtils.max(u1, xw - w2);
                int x2 = PtexUtils.min(u2, xw - w1);
                float U = (float)x1 - u;
                float V = (float)vi - v;
                float DQ = A * (2.0f * U + 1.0f) + B * V;
                float Q = A * U * U + (B * U + C * V) * V;
                T* p = (T*)data + (vi * rowlen + x1) * nTxChan;
                T* pEnd = p + (x2 - x1) * nTxChan;
                for (; p < pEnd; p += nTxChan)
                {
                    if (Q < 1.0f)
                    {
                        float w = gaussian(Q) * wscale;
                        weight += w;
                        PtexUtils.VecAccumN<T, TR>(result, p, nChan, w);
                    }
                    Q += DQ;
                    DQ += DDQ;
                }
            }
        }

        public void applyConst(float* dst, void* data, DataType dt, int nChan)
        {
            // iterate over texel locations and calculate weight as if texture weren't const
            float DDQ = 2.0f * A;
            for (int vi = v1; vi != v2; vi++)
            {
                int xw = rowlen - vi;
                int x1 = PtexUtils.max(u1, xw - w2);
                int x2 = PtexUtils.min(u2, xw - w1);
                float U = (float)x1 - u;
                float V = (float)vi - v;
                float DQ = A * (2.0f * U + 1.0f) + B * V;
                float Q = A * U * U + (B * U + C * V) * V;
                for (int x = x1; x < x2; x++)
                {
                    if (Q < 1.0f)
                    {
                        weight += gaussian(Q) * wscale;
                    }
                    Q += DQ;
                    DQ += DDQ;
                }
            }

            // apply weight to single texel value
            PtexUtils.applyConst(weight, dst, data, dt, nChan);
        }
    }

    // Triangle filter kernel (in normalized triangle coords)
    public struct PtexTriangleKernel
    {
        public Res res;                    // desired resolution
        public float u, v;                 // uv filter center
        public float u1, v1, w1;           // uvw lower bounds
        public float u2, v2, w2;           // uvw upper bounds
        public float A, B, C;              // ellipse coefficients (F = A*C-B*B/4)

        public void set(Res resVal, float uVal, float vVal,
                        float u1Val, float v1Val, float w1Val,
                        float u2Val, float v2Val, float w2Val,
                        float AVal, float BVal, float CVal)
        {
            res = resVal;
            u = uVal; v = vVal;
            u1 = u1Val; v1 = v1Val; w1 = w1Val;
            u2 = u2Val; v2 = v2Val; w2 = w2Val;
            A = AVal; B = BVal; C = CVal;
        }

        public void set(float uVal, float vVal,
                        float u1Val, float v1Val, float w1Val,
                        float u2Val, float v2Val, float w2Val)
        {
            u = uVal; v = vVal;
            u1 = u1Val; v1 = v1Val; w1 = w1Val;
            u2 = u2Val; v2 = v2Val; w2 = w2Val;
        }

        public void setABC(float AVal, float BVal, float CVal)
        {
            A = AVal; B = BVal; C = CVal;
        }

        public void splitU(out PtexTriangleKernel ka)
        {
            ka = this;
            u1 = 0;
            ka.u2 = 0;
        }

        public void splitV(out PtexTriangleKernel ka)
        {
            ka = this;
            v1 = 0;
            ka.v2 = 0;
        }

        public void splitW(out PtexTriangleKernel ka)
        {
            ka = this;
            w1 = 0;
            ka.w2 = 0;
        }

        public void rotate1()
        {
            // rotate ellipse where u'=w, v'=u, w'=v
            // (derived by converting to Barycentric form, rotating, and converting back)
            setABC(C, 2.0f * C - B, A + C - B);
        }

        public void rotate2()
        {
            // rotate ellipse where u'=v, v'=w, w'=u
            // (derived by converting to Barycentric form, rotating, and converting back)
            setABC(A + C - B, 2.0f * A - B, A);
        }

        public void reorient(int eid, int aeid)
        {
            float w = 1.0f - u - v;
            switch (eid * 3 + aeid)
            {
                case 0 * 3 + 0: set(1.0f - u, -v, 1.0f - u2, -v2, 1.0f - w2, 1.0f - u1, -v1, 1.0f - w1); break;
                case 0 * 3 + 1: set(1.0f - w, 1.0f - u, 1.0f - w2, 1.0f - u2, -v2, 1.0f - w1, 1.0f - u1, -v1); rotate1(); break;
                case 0 * 3 + 2: set(-v, 1.0f - w, -v2, 1.0f - w2, 1.0f - u2, -v1, 1.0f - w1, 1.0f - u1); rotate2(); break;
                case 1 * 3 + 0: set(1.0f - v, -w, 1.0f - v2, -w2, 1.0f - u2, 1.0f - v1, -w1, 1.0f - u1); rotate2(); break;
                case 1 * 3 + 1: set(1.0f - u, 1.0f - v, 1.0f - u2, 1.0f - v2, -w2, 1.0f - u1, 1.0f - v1, -w1); break;
                case 1 * 3 + 2: set(-w, 1.0f - u, -w2, 1.0f - u2, 1.0f - v2, -w1, 1.0f - u1, 1.0f - v1); rotate1(); break;
                case 2 * 3 + 0: set(1.0f - w, -u, 1.0f - w2, -u2, 1.0f - v2, 1.0f - w1, -u1, 1.0f - v1); rotate1(); break;
                case 2 * 3 + 1: set(1.0f - v, 1.0f - w, 1.0f - v2, 1.0f - w2, -u2, 1.0f - v1, 1.0f - w1, -u1); rotate2(); break;
                case 2 * 3 + 2: set(-u, 1.0f - v, -u2, 1.0f - v2, 1.0f - w2, -u1, 1.0f - v1, 1.0f - w1); break;
            }
        }

        public void clampRes(Res fres)
        {
            res.ulog2 = PtexUtils.min(res.ulog2, fres.ulog2);
            res.vlog2 = res.ulog2;
        }

        public void clampExtent()
        {
            u1 = PtexUtils.max(u1, 0.0f);
            v1 = PtexUtils.max(v1, 0.0f);
            w1 = PtexUtils.max(w1, 0.0f);
            u2 = PtexUtils.min(u2, 1.0f - (v1 + w1));
            v2 = PtexUtils.min(v2, 1.0f - (w1 + u1));
            w2 = PtexUtils.min(w2, 1.0f - (u1 + v1));
        }

        public void getIterators(out PtexTriangleKernelIter ke, out PtexTriangleKernelIter ko)
        {
            ke = new PtexTriangleKernelIter();
            ko = new PtexTriangleKernelIter();
            const float third = (float)(1 / 3.0);
            int resu = res.u();

            // normalize coefficients for texel units
            float Finv = 1.0f / ((float)resu * (float)resu * (A * C - 0.25f * B * B));
            float Ak = A * Finv, Bk = B * Finv, Ck = C * Finv;

            // build even iterator
            ke.rowlen = resu;
            ke.wscale = 1.0f / ((float)resu * (float)resu);
            float scale = (float)ke.rowlen;
            ke.u = u * scale - third;
            ke.v = v * scale - third;
            ke.u1 = (int)(PtexUtils.ceil(u1 * scale - third));
            ke.v1 = (int)(PtexUtils.ceil(v1 * scale - third));
            ke.w1 = (int)(PtexUtils.ceil(w1 * scale - third));
            ke.u2 = (int)(PtexUtils.ceil(u2 * scale - third));
            ke.v2 = (int)(PtexUtils.ceil(v2 * scale - third));
            ke.w2 = (int)(PtexUtils.ceil(w2 * scale - third));
            ke.A = Ak; ke.B = Bk; ke.C = Ck;
            ke.valid = (ke.u2 > ke.u1 && ke.v2 > ke.v1 && ke.w2 > ke.w1);
            ke.weight = 0;

            // build odd iterator: flip kernel across diagonal (u = 1-v, v = 1-u, w = -w)
            ko.rowlen = ke.rowlen;
            ko.wscale = ke.wscale;
            ko.u = (1.0f - v) * scale - third;
            ko.v = (1.0f - u) * scale - third;
            ko.u1 = (int)(PtexUtils.ceil((1.0f - v2) * scale - third));
            ko.v1 = (int)(PtexUtils.ceil((1.0f - u2) * scale - third));
            ko.w1 = (int)(PtexUtils.ceil((-w2) * scale - third));
            ko.u2 = (int)(PtexUtils.ceil((1.0f - v1) * scale - third));
            ko.v2 = (int)(PtexUtils.ceil((1.0f - u1) * scale - third));
            ko.w2 = (int)(PtexUtils.ceil((-w1) * scale - third));
            ko.A = Ck; ko.B = Bk; ko.C = Ak;
            ko.valid = (ko.u2 > ko.u1 && ko.v2 > ko.v1 && ko.w2 > ko.w1);
            ko.weight = 0;
        }
    }
}
