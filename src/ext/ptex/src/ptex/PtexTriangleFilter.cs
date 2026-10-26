// PtexTriangleFilter.h, PtexTriangleFilter.cpp
/*
PTEX SOFTWARE
Copyright 2014 Disney Enterprises, Inc.  All rights reserved
(see LICENSE.txt in ext/ptex)
*/

using System;

namespace Ptex
{
    public unsafe class PtexTriangleFilter : PtexFilter
    {
        static float squared(float x) { return x * x; }

        public PtexTriangleFilter(PtexTexture tx, PtexFilter.Options opts)
        {
            _tx = tx; _options = opts; _result = null; _weight = 0;
            _firstChanOffset = 0; _nchan = 0; _ntxchan = 0;
            _dt = (DataType)0;
        }

        public override void release() { }

        public override void eval(float* result, int firstChan, int nChannels,
                                  int faceid, float u, float v,
                                  float uw1, float vw1, float uw2, float vw2,
                                  float width, float blur)
        {
            // init
            if (_tx == null || nChannels <= 0) return;
            if (faceid < 0 || faceid >= _tx.numFaces()) return;
            _ntxchan = _tx.numChannels();
            _dt = _tx.dataType();
            _firstChanOffset = firstChan * PtexGlobals.DataSize(_dt);
            _nchan = PtexUtils.min(nChannels, _ntxchan - firstChan);

            // get face info
            FaceInfo f = _tx.getFaceInfo(faceid);

            // if neighborhood is constant, just return constant value of face
            if (f.isNeighborhoodConstant())
            {
                using (PtexPtr<PtexFaceData> data = new PtexPtr<PtexFaceData>(_tx.getData(faceid, new Res((ushort)0))))
                {
                    if (data.get() != null)
                    {
                        byte* d = (byte*)data.get().getData() + _firstChanOffset;
                        PtexGlobals.ConvertToFloat(result, d, _dt, _nchan);
                    }
                }
                return;
            }

            // clamp u and v
            u = PtexUtils.clamp(u, 0.0f, 1.0f);
            v = PtexUtils.clamp(v, 0.0f, 1.0f);

            // build kernel
            PtexTriangleKernel k = new PtexTriangleKernel();
            buildKernel(ref k, u, v, uw1, vw1, uw2, vw2, width, blur, f.res);

            // accumulate the weight as we apply
            _weight = 0;

            // allocate temporary result
            float* resultTmp = stackalloc float[_nchan];
            for (int i = 0; i < _nchan; i++) resultTmp[i] = 0;
            _result = resultTmp;

            // apply to faces
            splitAndApply(ref k, faceid, f);

            // normalize (both for data type and cumulative kernel weight applied)
            // and output result
            float scale = 1.0f / (_weight * PtexGlobals.OneValue(_dt));
            for (int i = 0; i < _nchan; i++) result[i] = (float)(_result[i] * scale);

            // clear temp result
            _result = null;
        }

        protected void buildKernel(ref PtexTriangleKernel k, float u, float v,
                                   float uw1, float vw1, float uw2, float vw2,
                                   float width, float blur, Res faceRes)
        {
            const float sqrt3 = 1.7320508075688772f;

            // compute ellipse coefficients, A*u^2 + B*u*v + C*v^2 == AC - B^2/4
            float scaleAC = 0.25f * width * width;
            float scaleB = -2.0f * scaleAC;
            float A = (vw1 * vw1 + vw2 * vw2) * scaleAC;
            float B = (uw1 * vw1 + uw2 * vw2) * scaleB;
            float C = (uw1 * uw1 + uw2 * uw2) * scaleAC;

            // convert to cartesian domain
            float Ac = 0.75f * A;
            float Bc = (float)(sqrt3 / 2) * (B - A);
            float Cc = 0.25f * A - 0.5f * B + C;

            // compute min blur for eccentricity clamping
            const float maxEcc = 15.0f;  // max eccentricity
            const float eccRatio = (maxEcc * maxEcc + 1.0f) / (maxEcc * maxEcc - 1.0f);
            float X = MathF.Sqrt(squared(Ac - Cc) + squared(Bc));
            float b_e = 0.5f * (eccRatio * X - (Ac + Cc));

            // compute min blur for texel clamping
            // (ensure that ellipse is no smaller than a texel)
            float b_t = squared(0.5f / (float)faceRes.u());

            // add blur
            float b_b = 0.25f * blur * blur;
            float b = PtexUtils.max(b_b, PtexUtils.max(b_e, b_t));
            Ac += b;
            Cc += b;

            // compute minor radius
            float m = MathF.Sqrt(2.0f * (Ac * Cc - 0.25f * Bc * Bc) / (Ac + Cc + X));

            // choose desired resolution
            int reslog2 = PtexUtils.max(0, PtexUtils.calcResFromWidth(2.0f * m));

            // convert back to triangular domain
            A = (float)(4 / 3.0) * Ac;
            B = (float)(2 / sqrt3) * Bc + A;
            C = -0.25f * A + 0.5f * B + Cc;

            // scale by kernel width
            float scale = PtexGlobals.PtexTriangleKernelWidth * PtexGlobals.PtexTriangleKernelWidth;
            A *= scale;
            B *= scale;
            C *= scale;

            // find u,v,w extents
            float uw = PtexUtils.min(MathF.Sqrt(C), 1.0f);
            float vw = PtexUtils.min(MathF.Sqrt(A), 1.0f);
            float ww = PtexUtils.min(MathF.Sqrt(A - B + C), 1.0f);

            // init kernel
            float w = 1.0f - u - v;
            k.set(new Res((sbyte)reslog2, (sbyte)reslog2), u, v, u - uw, v - vw, w - ww, u + uw, v + vw, w + ww, A, B, C);
        }

        protected void splitAndApply(ref PtexTriangleKernel k, int faceid, FaceInfo f)
        {
            // do we need to split? if so, split kernel and apply across edge(s)
            if (k.u1 < 0 && f.adjface(2) >= 0)
            {
                PtexTriangleKernel ka;
                k.splitU(out ka);
                applyAcrossEdge(ref ka, f, 2);
            }
            if (k.v1 < 0 && f.adjface(0) >= 0)
            {
                PtexTriangleKernel ka;
                k.splitV(out ka);
                applyAcrossEdge(ref ka, f, 0);
            }
            if (k.w1 < 0 && f.adjface(1) >= 0)
            {
                PtexTriangleKernel ka;
                k.splitW(out ka);
                applyAcrossEdge(ref ka, f, 1);
            }

            // apply to local face
            apply(ref k, faceid, f);
        }

        protected void applyAcrossEdge(ref PtexTriangleKernel k, FaceInfo f, int eid)
        {
            int afid = f.adjface(eid), aeid = (int)f.adjedge(eid);
            FaceInfo af = _tx.getFaceInfo(afid);
            k.reorient(eid, aeid);
            splitAndApply(ref k, afid, af);
        }

        protected void apply(ref PtexTriangleKernel k, int faceid, FaceInfo f)
        {
            // clamp kernel face (resolution and extent)
            k.clampRes(f.res);
            k.clampExtent();

            // build kernel iterators
            PtexTriangleKernelIter keven, kodd;
            k.getIterators(out keven, out kodd);
            if (!keven.valid && !kodd.valid) return;

            // get face data, and apply
            using (PtexPtr<PtexFaceData> dh = new PtexPtr<PtexFaceData>(_tx.getData(faceid, k.res)))
            {
                if (dh.get() == null) return;

                if (keven.valid) applyIter(ref keven, dh.get());
                if (kodd.valid) applyIter(ref kodd, dh.get());
            }
        }

        protected void applyIter(ref PtexTriangleKernelIter k, PtexFaceData dh)
        {
            if (dh.isConstant())
            {
                k.applyConst(_result, (byte*)dh.getData() + _firstChanOffset, _dt, _nchan);
                _weight += k.weight;
            }
            else if (dh.isTiled())
            {
                Res tileres = dh.tileRes();
                PtexTriangleKernelIter kt = k;
                int tileresu = tileres.u();
                int tileresv = tileres.v();
                kt.rowlen = tileresu;
                int ntilesu = k.rowlen / kt.rowlen;
                int wOffsetBase = k.rowlen - tileresu;
                for (int tilev = k.v1 / tileresv, tilevEnd = (k.v2 - 1) / tileresv; tilev <= tilevEnd; tilev++)
                {
                    int vOffset = tilev * tileresv;
                    kt.v = k.v - (float)vOffset;
                    kt.v1 = PtexUtils.max(0, k.v1 - vOffset);
                    kt.v2 = PtexUtils.min(k.v2 - vOffset, tileresv);
                    for (int tileu = k.u1 / tileresu, tileuEnd = (k.u2 - 1) / tileresu; tileu <= tileuEnd; tileu++)
                    {
                        int uOffset = tileu * tileresu;
                        int wOffset = wOffsetBase - uOffset - vOffset;
                        kt.u = k.u - (float)uOffset;
                        kt.u1 = PtexUtils.max(0, k.u1 - uOffset);
                        kt.u2 = PtexUtils.min(k.u2 - uOffset, tileresu);
                        kt.w1 = k.w1 - wOffset;
                        kt.w2 = k.w2 - wOffset;
                        using (PtexPtr<PtexFaceData> th = new PtexPtr<PtexFaceData>(dh.getTile(tilev * ntilesu + tileu)))
                        {
                            if (th.get() != null)
                            {
                                kt.weight = 0;
                                if (th.get().isConstant())
                                    kt.applyConst(_result, (byte*)th.get().getData() + _firstChanOffset, _dt, _nchan);
                                else
                                    kt.apply(_result, (byte*)th.get().getData() + _firstChanOffset, _dt, _nchan, _ntxchan);
                                _weight += kt.weight;
                            }
                        }
                    }
                }
            }
            else
            {
                k.apply(_result, (byte*)dh.getData() + _firstChanOffset, _dt, _nchan, _ntxchan);
                _weight += k.weight;
            }
        }

        protected PtexTexture _tx;            // texture being evaluated
        protected Options _options;           // options
        protected float* _result;             // temp result
        protected float _weight;              // accumulated weight of data in _result
        protected int _firstChanOffset;       // byte offset of first channel to eval
        protected int _nchan;                 // number of channels to eval
        protected int _ntxchan;               // number of channels in texture
        protected DataType _dt;               // data type of texture
    }
}
