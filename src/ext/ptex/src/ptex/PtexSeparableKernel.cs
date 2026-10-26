// PtexSeparableKernel.h, PtexSeparableKernel.cpp
/*
PTEX SOFTWARE
Copyright 2014 Disney Enterprises, Inc.  All rights reserved
(see LICENSE.txt in ext/ptex)
*/

// Separable convolution kernel
//
// Port notes: ku/kv are float* into either kubuff/kvbuff or (after swapUV, or for the tile
// sub-kernels) into another kernel's buffers; they are represented as (array, offset) pairs.
// The per-type/per-channel-count unrolled apply functions of the C++ perform identical
// per-channel float operations in identical order, so one generic implementation (strided by
// nTxChan) is used for all of them.

using System;

namespace Ptex
{
    public unsafe sealed class PtexSeparableKernel
    {
        public Res res;                    // resolution that kernel was built for
        public int u, v;                   // uv offset within face data
        public int uw, vw;                 // kernel width
        public float[] kuA;                // kernel weights in u (array and offset: float* ku)
        public int kuO;
        public float[] kvA;                // kernel weights in v
        public int kvO;
        public const int kmax = 10;        // max kernel width
        // (oversized so that the unchecked writes of upres/downres stay in bounds)
        public float[] kubuff = new float[kmax * 2];
        public float[] kvbuff = new float[kmax * 2];
        public int rot;

        public PtexSeparableKernel()
        {
            res = new Res((ushort)0); u = 0; v = 0; uw = 0; vw = 0;
            kuA = kubuff; kuO = 0; kvA = kvbuff; kvO = 0; rot = 0;
            kubuff[0] = 0;
            kvbuff[0] = 0;
        }

        public PtexSeparableKernel(PtexSeparableKernel k)
        {
            kuA = kubuff; kvA = kvbuff;
            set(k.res, k.u, k.v, k.uw, k.vw, k.kuA, k.kuO, k.kvA, k.kvO, k.rot);
        }

        // operator=
        public void copyFrom(PtexSeparableKernel k)
        {
            set(k.res, k.u, k.v, k.uw, k.vw, k.kuA, k.kuO, k.kvA, k.kvO, k.rot);
        }

        public void set(Res resVal,
                        int uVal, int vVal,
                        int uwVal, int vwVal,
                        float[] kuVal, int kuValO, float[] kvVal, int kvValO, int rotVal = 0)
        {
            System.Diagnostics.Debug.Assert(uwVal <= kmax && vwVal <= kmax);
            res = resVal;
            u = uVal;
            v = vVal;
            uw = uwVal;
            vw = vwVal;
            Array.Copy(kuVal, kuValO, kubuff, 0, uw);
            Array.Copy(kvVal, kvValO, kvbuff, 0, vw);
            kuA = kubuff; kuO = 0;
            kvA = kvbuff; kvO = 0;
            rot = rotVal;
        }

        // (helpers for the float* ku / kv accesses)
        public float ku(int i) { return kuA[kuO + i]; }
        public float kv(int i) { return kvA[kvO + i]; }

        public void stripZeros()
        {
            while (kuA[kuO] == 0) { kuO++; u++; uw--; }
            while (kuA[kuO + uw - 1] == 0) { uw--; }
            while (kvA[kvO] == 0) { kvO++; v++; vw--; }
            while (kvA[kvO + vw - 1] == 0) { vw--; }
            System.Diagnostics.Debug.Assert(uw > 0 && vw > 0);
        }

        public float weight()
        {
            return accumulate(kuA, kuO, uw) * accumulate(kvA, kvO, vw);
        }

        public void mergeL(BorderMode mode)
        {
            int w = -u;
            if (mode != BorderMode.m_black)
                kuA[kuO + w] += accumulate(kuA, kuO, w);
            kuO += w;
            uw -= w;
            u = 0;
        }

        public void mergeR(BorderMode mode)
        {
            int w = uw + u - res.u();
            int kp = kuO + uw - w;
            if (mode != BorderMode.m_black)
                kuA[kp - 1] += accumulate(kuA, kp, w);
            uw -= w;
        }

        public void mergeB(BorderMode mode)
        {
            int w = -v;
            if (mode != BorderMode.m_black)
                kvA[kvO + w] += accumulate(kvA, kvO, w);
            kvO += w;
            vw -= w;
            v = 0;
        }

        public void mergeT(BorderMode mode)
        {
            int w = vw + v - res.v();
            int kp = kvO + vw - w;
            if (mode != BorderMode.m_black)
                kvA[kp - 1] += accumulate(kvA, kp, w);
            vw -= w;
        }

        public void splitL(PtexSeparableKernel k)
        {
            // split off left piece of width w into k
            int w = -u;
            if (w < uw)
            {
                // normal case - split off a portion
                //    res  u          v  uw vw  ku  kv
                k.set(res, res.u() - w, v, w, vw, kuA, kuO, kvA, kvO);

                // update local
                u = 0;
                uw -= w;
                kuO += w;
            }
            else
            {
                // entire kernel is split off
                k.copyFrom(this);
                k.u += res.u();
                u = 0; uw = 0;
            }
        }

        public void splitR(PtexSeparableKernel k)
        {
            // split off right piece of width w into k
            int w = u + uw - res.u();
            if (w < uw)
            {
                // normal case - split off a portion
                //    res  u  v  uw vw  ku           kv
                k.set(res, 0, v, w, vw, kuA, kuO + uw - w, kvA, kvO);

                // update local
                uw -= w;
            }
            else
            {
                // entire kernel is split off
                k.copyFrom(this);
                k.u -= res.u();
                u = 0; uw = 0;
            }
        }

        public void splitB(PtexSeparableKernel k)
        {
            // split off bottom piece of width w into k
            int w = -v;
            if (w < vw)
            {
                // normal case - split off a portion
                //    res  u  v          uw vw  ku  kv
                k.set(res, u, res.v() - w, uw, w, kuA, kuO, kvA, kvO);

                // update local
                v = 0;
                vw -= w;
                kvO += w;
            }
            else
            {
                // entire kernel is split off
                k.copyFrom(this);
                k.v += res.v();
                v = 0; vw = 0;
            }
        }

        public void splitT(PtexSeparableKernel k)
        {
            // split off top piece of width w into k
            int w = v + vw - res.v();
            if (w < vw)
            {
                // normal case - split off a portion
                //    res  u  v  uw vw  ku  kv
                k.set(res, u, 0, uw, w, kuA, kuO, kvA, kvO + vw - w);

                // update local
                vw -= w;
            }
            else
            {
                // entire kernel is split off
                k.copyFrom(this);
                k.v -= res.v();
                v = 0; vw = 0;
            }
        }

        public void flipU()
        {
            u = res.u() - u - uw;
            Array.Reverse(kuA, kuO, uw);
        }

        public void flipV()
        {
            v = res.v() - v - vw;
            Array.Reverse(kvA, kvO, vw);
        }

        public void swapUV()
        {
            res.swapuv();
            int t = u; u = v; v = t;
            t = uw; uw = vw; vw = t;
            float[] ta = kuA; kuA = kvA; kvA = ta;
            t = kuO; kuO = kvO; kvO = t;
        }

        public void rotate(int rotVal)
        {
            // rotate kernel 'rot' steps ccw
            switch (rotVal & 3)
            {
                default: return;
                case 1: flipU(); swapUV(); break;
                case 2: flipU(); flipV(); break;
                case 3: flipV(); swapUV(); break;
            }
            rot = (rot + rotVal) & 3;
        }

        public bool adjustMainToSubface(int eid)
        {
            // to adjust the kernel for the subface, we must adjust the res down and offset the uv coords
            // however, if the res is already zero, we must upres the kernel first
            if (res.ulog2 == 0) upresU();
            if (res.vlog2 == 0) upresV();
            if (res.ulog2 > 0) res.ulog2--;
            if (res.vlog2 > 0) res.vlog2--;

            // offset uv coords and determine whether target subface is the primary one
            bool primary = false;
            int resu = res.u(), resv = res.v();
            switch (eid & 3)
            {
                case (int)EdgeId.e_bottom:
                    primary = (u < resu);
                    v -= resv;
                    if (!primary) u -= resu;
                    break;
                case (int)EdgeId.e_right:
                    primary = (v < resv);
                    if (!primary) v -= resv;
                    break;
                case (int)EdgeId.e_top:
                    primary = (u >= resu);
                    if (primary) u -= resu;
                    break;
                case (int)EdgeId.e_left:
                    primary = (v >= resv);
                    u -= resu;
                    if (primary) v -= resv;
                    break;
            }
            return primary;
        }

        public void adjustSubfaceToMain(int eid)
        {
            switch (eid & 3)
            {
                case (int)EdgeId.e_bottom: v += res.v(); break;
                case (int)EdgeId.e_right: break;
                case (int)EdgeId.e_top: u += res.u(); break;
                case (int)EdgeId.e_left: u += res.u(); v += res.v(); break;
            }
            res.ulog2++; res.vlog2++;
        }

        public void downresU()
        {
            int src = kuO;
            int dst = kuO;
            float[] a = kuA;

            // skip odd leading sample (if any)
            if ((u & 1) != 0)
            {
                dst++;
                src++;
                uw--;
            }

            // combine even pairs
            for (int i = uw / 2; i > 0; i--)
            {
                a[dst++] = a[src] + a[src + 1];
                src += 2;
            }

            // copy odd trailing sample (if any)
            if ((uw & 1) != 0)
            {
                a[dst++] = a[src++];
            }

            // update state
            u /= 2;
            uw = dst - kuO;
            res.ulog2--;
        }

        public void downresV()
        {
            int src = kvO;
            int dst = kvO;
            float[] a = kvA;

            // skip odd leading sample (if any)
            if ((v & 1) != 0)
            {
                dst++;
                src++;
                vw--;
            }

            // combine even pairs
            for (int i = vw / 2; i > 0; i--)
            {
                a[dst++] = a[src] + a[src + 1];
                src += 2;
            }

            // copy odd trailing sample (if any)
            if ((vw & 1) != 0)
            {
                a[dst++] = a[src++];
            }

            // update state
            v /= 2;
            vw = dst - kvO;
            res.vlog2--;
        }

        public void upresU()
        {
            int src = kuO + uw - 1;
            int dst = kuO + uw * 2 - 2;
            float[] a = kuA;
            for (int i = uw; i > 0; i--)
            {
                a[dst] = a[dst + 1] = a[src--] / 2;
                dst -= 2;
            }
            uw *= 2;
            u *= 2;
            res.ulog2++;
        }

        public void upresV()
        {
            int src = kvO + vw - 1;
            int dst = kvO + vw * 2 - 2;
            float[] a = kvA;
            for (int i = vw; i > 0; i--)
            {
                a[dst] = a[dst + 1] = a[src--] / 2;
                dst -= 2;
            }
            vw *= 2;
            v *= 2;
            res.vlog2++;
        }

        public float makeSymmetric(float initialWeight)
        {
            System.Diagnostics.Debug.Assert(u == 0 && v == 0);

            // downres higher-res dimension until equal
            if (res.ulog2 > res.vlog2)
            {
                do { downresU(); } while (res.ulog2 > res.vlog2);
            }
            else if (res.vlog2 > res.ulog2)
            {
                do { downresV(); } while (res.vlog2 > res.ulog2);
            }

            // truncate excess samples in longer dimension
            uw = vw = PtexUtils.min(uw, vw);

            // combine corresponding u and v samples and compute new kernel weight
            float newWeight = 0;
            for (int i = 0; i < uw; i++)
            {
                float sum = kuA[kuO + i] + kvA[kvO + i];
                kuA[kuO + i] = kvA[kvO + i] = sum;
                newWeight += sum;
            }
            newWeight *= newWeight;  // equivalent to k.weight() ( = sum(ku)*sum(kv) )

            // compute scale factor to compensate for weight change
            float scale = newWeight == 0 ? 1.0f : initialWeight / newWeight;

            // Note: a sharpening kernel (like Mitchell) can produce
            // negative weights which may cancel out when adding the two
            // kernel axes together, and this can cause the compensation
            // scale factor to spike up.  We expect the scale factor to be
            // less than one in "normal" cases (i.e. ku*kv <= (ku+kv)^2 if ku
            // and kv are both positive), so clamping to -1..1 will have
            // no effect on positive kernels.  If there are negative
            // weights, the clamping will just limit the amount of
            // sharpening happening at the corners, and the result will
            // still be smooth.

            // clamp scale factor to -1..1 range
            if (scale >= 1)
            {
                // scale by 1 (i.e. do nothing)
            }
            else
            {
                if (scale < -1)
                {
                    // a negative scale means the original kernel had an overall negative weight
                    // after making symmetric, the kernel will always be positive
                    // scale ku by -1
                    // note: choice of u is arbitrary; we could have scaled u or v (but not both)
                    for (int i = 0; i < uw; i++) kuA[kuO + i] *= -1;
                    newWeight = -newWeight;
                }
                else
                {
                    // scale ku to restore initialWeight (again, choice of u instead of v is arbitrary)
                    for (int i = 0; i < uw; i++) kuA[kuO + i] *= scale;
                    newWeight = initialWeight;
                }
            }

            return newWeight;
        }

        public void apply(float* dst, void* data, DataType dt, int nChan, int nTxChan)
        {
            switch (dt)
            {
                case DataType.dt_uint8: Apply<byte, PixU8>(dst, data, nChan, nTxChan); break;
                case DataType.dt_uint16: Apply<ushort, PixU16>(dst, data, nChan, nTxChan); break;
                case DataType.dt_half: Apply<PtexHalf, PixHalf>(dst, data, nChan, nTxChan); break;
                default: Apply<float, PixFloat>(dst, data, nChan, nTxChan); break;
            }
        }

        public void applyConst(float* dst, void* data, DataType dt, int nChan)
        {
            PtexUtils.applyConst(weight(), dst, data, dt, nChan);
        }

        // apply to nChan channels of data with pixel stride nTxChan
        void Apply<T, TR>(float* result, void* data, int nChan, int nTxChan) where T : unmanaged where TR : struct, IPtexPixel<T>
        {
            float* rowResult = stackalloc float[Math.Max(nChan, 1)];
            int rowlen = res.u() * nTxChan;
            int datalen = uw * nTxChan;
            int rowskip = rowlen - datalen;
            int kvp = kvO;

            T* p = (T*)data + (v * res.u() + u) * nTxChan;
            T* pEnd = p + vw * rowlen;
            while (p != pEnd)
            {
                int kup = kuO;
                T* pRowEnd = p + datalen;

                // just mult and copy first element
                PtexUtils.VecMultN<T, TR>(rowResult, p, nChan, kuA[kup++]);
                p += nTxChan;

                // accumulate remaining elements
                while (p != pRowEnd)
                {
                    // rowResult[i] = p[i] * ku[u] for i in {0..n-1}
                    PtexUtils.VecAccumN<T, TR>(rowResult, p, nChan, kuA[kup++]);
                    p += nTxChan;
                }

                // result[i] += rowResult[i] * kv[v] for i in {0..n-1}
                PtexUtils.VecAccumN<float, PixFloat>(result, rowResult, nChan, kvA[kvp++]);
                p += rowskip;
            }
        }

        static float accumulate(float[] a, int off, int n)
        {
            float result = 0;
            for (int i = 0; i < n; i++) result += a[off + i];
            return result;
        }
    }
}
