// PtexSeparableFilter.h, PtexSeparableFilter.cpp
/*
PTEX SOFTWARE
Copyright 2014 Disney Enterprises, Inc.  All rights reserved
(see LICENSE.txt in ext/ptex)
*/

using System;

namespace Ptex
{
    public abstract unsafe class PtexSeparableFilter : PtexFilter
    {
        public override void release() { }

        protected PtexSeparableFilter(PtexTexture tx, PtexFilter.Options opts)
        {
            _tx = tx; _options = opts; _result = null; _weight = 0;
            _firstChanOffset = 0; _nchan = 0; _ntxchan = _tx.numChannels();
            _dt = tx.dataType(); _uMode = tx.uBorderMode(); _vMode = tx.vBorderMode();
            _efm = tx.edgeFilterMode();

            // if caller was compiled with older version of struct, set default for new opts
            if (_options.__structSize < 16 /* offsetof(Options, noedgeblend) */)
            {
                _options.noedgeblend = false;
            }
        }

        protected abstract void buildKernel(PtexSeparableKernel k, float u, float v, float uw, float vw, Res faceRes);

        public override void eval(float* result, int firstChan, int nChannels,
                                  int faceid, float u, float v,
                                  float uw1, float vw1, float uw2, float vw2,
                                  float width, float blur)
        {
            // init
            if (_tx == null || nChannels <= 0) return;
            if (faceid < 0 || faceid >= _tx.numFaces()) return;
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

            // find filter width as bounding box of vectors w1 and w2
            float uw = PtexUtils.abs(uw1) + PtexUtils.abs(uw2), vw = PtexUtils.abs(vw1) + PtexUtils.abs(vw2);

            // handle border modes
            bool return_black = false;
            switch (_uMode)
            {
                case BorderMode.m_clamp: u = PtexUtils.clamp(u, 0.0f, 1.0f); break;
                case BorderMode.m_periodic: u = u - PtexUtils.floor(u); break;
                case BorderMode.m_black: if (u <= -1.0f || u >= 2.0f) return_black = true; break;
            }
            switch (_vMode)
            {
                case BorderMode.m_clamp: v = PtexUtils.clamp(v, 0.0f, 1.0f); break;
                case BorderMode.m_periodic: v = v - PtexUtils.floor(v); break;
                case BorderMode.m_black: if (v <= -1.0f || v >= 2.0f) return_black = true; break;
            }
            if (return_black)
            {
                for (int i = 0; i < _nchan; i++) result[i] = 0;
                return;
            }

            // build kernel
            PtexSeparableKernel k = new PtexSeparableKernel();
            if (f.isSubface())
            {
                // for a subface, build the kernel as if it were on a main face and then downres
                uw = uw * width + blur * 2.0f;
                vw = vw * width + blur * 2.0f;
                buildKernel(k, u * .5f, v * .5f, uw * .5f, vw * .5f,
                            new Res((sbyte)(f.res.ulog2 + 1), (sbyte)(f.res.vlog2 + 1)));
                if (k.res.ulog2 == 0) k.upresU();
                if (k.res.vlog2 == 0) k.upresV();
                k.res.ulog2--; k.res.vlog2--;
            }
            else
            {
                uw = uw * width + blur;
                vw = vw * width + blur;
                buildKernel(k, u, v, uw, vw, f.res);
            }
            k.stripZeros();

            // check kernel (debug only)
            System.Diagnostics.Debug.Assert(k.uw > 0 && k.vw > 0);
            System.Diagnostics.Debug.Assert(k.uw <= PtexSeparableKernel.kmax && k.vw <= PtexSeparableKernel.kmax);

            _weight = k.weight();

            // allocate temporary result
            float* resultTmp = stackalloc float[_nchan];
            for (int i = 0; i < _nchan; i++) resultTmp[i] = 0;
            _result = resultTmp;

            // apply to faces
            splitAndApply(k, faceid, f);

            // normalize (both for data type and cumulative kernel weight applied)
            // and output result
            float scale = 1.0f / (_weight * PtexGlobals.OneValue(_dt));
            for (int i = 0; i < _nchan; i++) result[i] = (float)(_result[i] * scale);

            // clear temp result
            _result = null;
        }

        protected void splitAndApply(PtexSeparableKernel k, int faceid, FaceInfo f)
        {
            // do we need to split? (i.e. does kernel span an edge?)
            bool splitR = (k.u + k.uw > k.res.u()), splitL = (k.u < 0);
            bool splitT = (k.v + k.vw > k.res.v()), splitB = (k.v < 0);

            if (_options.noedgeblend)
            {
                if (splitR) k.mergeR(_uMode);
                if (splitL) k.mergeL(_uMode);
                if (splitT) k.mergeT(_vMode);
                if (splitB) k.mergeB(_vMode);
                apply(k, faceid, f);
                return;
            }

            if (splitR || splitL || splitT || splitB)
            {
                PtexSeparableKernel ka = new PtexSeparableKernel(), kc = new PtexSeparableKernel();
                if (splitR)
                {
                    if (f.adjface((int)EdgeId.e_right) >= 0)
                    {
                        k.splitR(ka);
                        if (splitT)
                        {
                            if (f.adjface((int)EdgeId.e_top) >= 0)
                            {
                                ka.splitT(kc);
                                applyToCorner(kc, faceid, f, (int)EdgeId.e_top);
                            }
                            else ka.mergeT(_vMode);
                        }
                        if (splitB)
                        {
                            if (f.adjface((int)EdgeId.e_bottom) >= 0)
                            {
                                ka.splitB(kc);
                                applyToCorner(kc, faceid, f, (int)EdgeId.e_right);
                            }
                            else ka.mergeB(_vMode);
                        }
                        applyAcrossEdge(ka, faceid, f, (int)EdgeId.e_right);
                    }
                    else k.mergeR(_uMode);
                }
                if (splitL)
                {
                    if (f.adjface((int)EdgeId.e_left) >= 0)
                    {
                        k.splitL(ka);
                        if (splitT)
                        {
                            if (f.adjface((int)EdgeId.e_top) >= 0)
                            {
                                ka.splitT(kc);
                                applyToCorner(kc, faceid, f, (int)EdgeId.e_left);
                            }
                            else ka.mergeT(_vMode);
                        }
                        if (splitB)
                        {
                            if (f.adjface((int)EdgeId.e_bottom) >= 0)
                            {
                                ka.splitB(kc);
                                applyToCorner(kc, faceid, f, (int)EdgeId.e_bottom);
                            }
                            else ka.mergeB(_vMode);
                        }
                        applyAcrossEdge(ka, faceid, f, (int)EdgeId.e_left);
                    }
                    else k.mergeL(_uMode);
                }
                if (splitT)
                {
                    if (f.adjface((int)EdgeId.e_top) >= 0)
                    {
                        k.splitT(ka);
                        applyAcrossEdge(ka, faceid, f, (int)EdgeId.e_top);
                    }
                    else k.mergeT(_vMode);
                }
                if (splitB)
                {
                    if (f.adjface((int)EdgeId.e_bottom) >= 0)
                    {
                        k.splitB(ka);
                        applyAcrossEdge(ka, faceid, f, (int)EdgeId.e_bottom);
                    }
                    else k.mergeB(_vMode);
                }
            }

            // do local face
            apply(k, faceid, f);
        }

        protected void applyAcrossEdge(PtexSeparableKernel k, int faceid, FaceInfo f, int eid)
        {
            int afid = f.adjface(eid), aeid = (int)f.adjedge(eid);
            FaceInfo af = _tx.getFaceInfo(afid);

            int rot = eid - aeid + 2;

            // adjust uv coord and res for face/subface boundary
            bool fIsSubface = f.isSubface(), afIsSubface = af.isSubface();
            if (fIsSubface != afIsSubface)
            {
                if (afIsSubface)
                {
                    // main face to subface transition
                    // adjust res and offset uv coord for primary subface
                    bool primary = k.adjustMainToSubface(eid);
                    if (!primary)
                    {
                        // advance ajacent face and edge id to secondary subface
                        int neid = (aeid + 3) % 4;
                        afid = af.adjface(neid);
                        aeid = (int)af.adjedge(neid);
                        af = _tx.getFaceInfo(afid);
                        rot += neid - aeid + 2;
                    }
                }
                else
                {
                    // subface to main face transition
                    // Note: the transform depends on which subface the kernel is
                    // coming from.  The "primary" subface is the one the main
                    // face is pointing at.  The secondary subface adjustment
                    // happens to be the same as for the primary subface for the
                    // next edge, so the cases can be combined.
                    bool primary = (af.adjface(aeid) == faceid);
                    k.adjustSubfaceToMain(eid - (primary ? 1 : 0));
                }
            }

            // rotate and apply (resplit if going to a subface)
            k.rotate(rot);
            if (afIsSubface) splitAndApply(k, afid, af);
            else apply(k, afid, af);
        }

        protected void applyToCorner(PtexSeparableKernel k, int faceid, FaceInfo f, int eid)
        {
            // traverse clockwise around corner vertex and gather corner faces
            int afid = faceid, aeid = eid;
            FaceInfo af = f;
            bool prevIsSubface = af.isSubface();

            const int MaxValence = 10;
            int[] cfaceId = new int[MaxValence];
            int[] cedgeId = new int[MaxValence];
            FaceInfo[] cface = new FaceInfo[MaxValence];
            int numCorners = 0;
            for (int i = 0; i < MaxValence; i++)
            {
                // advance to next face
                int prevFace = afid;
                afid = af.adjface(aeid);
                aeid = ((int)af.adjedge(aeid) + 1) % 4;

                // we hit a boundary or reached starting face
                // note: we need to check edge id too because we might have
                // a periodic texture (w/ toroidal topology) where all 4 corners
                // are from the same face
                if (afid < 0 || (afid == faceid && aeid == eid))
                {
                    numCorners = i - 2;
                    break;
                }

                // record face info
                af = _tx.getFaceInfo(afid);
                cfaceId[i] = afid;
                cedgeId[i] = aeid;
                cface[i] = af;

                // check to see if corner is a subface "tee"
                bool isSubface = af.isSubface();
                if (prevIsSubface && !isSubface && af.adjface((aeid + 3) % 4) == prevFace)
                {
                    // adjust the eid depending on whether we started from
                    // the primary or secondary subface.
                    bool primary = (i == 1);
                    k.adjustSubfaceToMain(eid + (primary ? 2 : 0));
                    k.rotate(eid - aeid + 3 - (primary ? 1 : 0));
                    splitAndApply(k, afid, af);
                    return;
                }
                prevIsSubface = isSubface;
            }

            if (numCorners == 1)
            {
                // regular case (valence 4)
                applyToCornerFace(k, f, eid, cfaceId[1], cface[1], cedgeId[1]);
            }
            else if (numCorners > 1)
            {
                // valence 5+, make kernel symmetric and apply equally to each face

                // first, rotate to standard orientation, u=v=0
                k.rotate(eid + 2);

                float initialWeight = k.weight();
                float newWeight = k.makeSymmetric(initialWeight);

                for (int i = 1; i <= numCorners; i++)
                {
                    PtexSeparableKernel kc = new PtexSeparableKernel(k);
                    applyToCornerFace(kc, f, 2, cfaceId[i], cface[i], cedgeId[i]);
                }

                // adjust weight for symmetrification and for additional corners
                _weight += newWeight * (float)numCorners - initialWeight;
            }
            else
            {
                // valence 2 or 3, ignore corner face (just adjust weight)
                _weight -= k.weight();
            }
        }

        protected void applyToCornerFace(PtexSeparableKernel k, FaceInfo f, int eid,
                                         int cfid, FaceInfo cf, int ceid)
        {
            // adjust uv coord and res for face/subface boundary
            bool fIsSubface = f.isSubface(), cfIsSubface = cf.isSubface();
            if (fIsSubface != cfIsSubface)
            {
                if (cfIsSubface) k.adjustMainToSubface(eid + 3);
                else k.adjustSubfaceToMain(eid + 3);
            }

            // rotate and apply (resplit if going to a subface)
            k.rotate(eid - ceid + 2);
            if (cfIsSubface) splitAndApply(k, cfid, cf);
            else apply(k, cfid, cf);
        }

        protected void apply(PtexSeparableKernel k, int faceid, FaceInfo f)
        {
            System.Diagnostics.Debug.Assert(k.u >= 0 && k.u + k.uw <= k.res.u());
            System.Diagnostics.Debug.Assert(k.v >= 0 && k.v + k.vw <= k.res.v());
            if (k.uw <= 0 || k.vw <= 0) return;

            // downres kernel if needed
            while (k.res.u() > f.res.u()) k.downresU();
            while (k.res.v() > f.res.v()) k.downresV();

            // get face data, and apply
            using (PtexPtr<PtexFaceData> dhp = new PtexPtr<PtexFaceData>(_tx.getData(faceid, k.res)))
            {
                PtexFaceData dh = dhp.get();
                if (dh == null) return;

                if (dh.isConstant())
                {
                    k.applyConst(_result, (byte*)dh.getData() + _firstChanOffset, _dt, _nchan);
                    return;
                }

                // allocate temporary result for tanvec mode (if needed)
                bool tanvecMode = (_efm == EdgeFilterMode.efm_tanvec) && (_nchan >= 2) && (k.rot > 0);
                float* tanResult = stackalloc float[_nchan];
                float* result = tanvecMode ? tanResult : _result;
                if (tanvecMode) for (int i = 0; i < _nchan; i++) result[i] = 0;

                if (dh.isTiled())
                {
                    Res tileres = dh.tileRes();
                    PtexSeparableKernel kt = new PtexSeparableKernel();
                    kt.res = tileres;
                    int tileresu = tileres.u();
                    int tileresv = tileres.v();
                    int ntilesu = k.res.u() / tileresu;
                    for (int v = k.v, vw = k.vw; vw > 0; vw -= kt.vw, v += kt.vw)
                    {
                        int tilev = v / tileresv;
                        kt.v = v % tileresv;
                        kt.vw = PtexUtils.min(vw, tileresv - kt.v);
                        kt.kvA = k.kvA; kt.kvO = k.kvO + v - k.v;
                        for (int u = k.u, uw = k.uw; uw > 0; uw -= kt.uw, u += kt.uw)
                        {
                            int tileu = u / tileresu;
                            kt.u = u % tileresu;
                            kt.uw = PtexUtils.min(uw, tileresu - kt.u);
                            kt.kuA = k.kuA; kt.kuO = k.kuO + u - k.u;
                            using (PtexPtr<PtexFaceData> thp = new PtexPtr<PtexFaceData>(dh.getTile(tilev * ntilesu + tileu)))
                            {
                                PtexFaceData th = thp.get();
                                if (th != null)
                                {
                                    if (th.isConstant())
                                        kt.applyConst(result, (byte*)th.getData() + _firstChanOffset, _dt, _nchan);
                                    else
                                        kt.apply(result, (byte*)th.getData() + _firstChanOffset, _dt, _nchan, _ntxchan);
                                }
                            }
                        }
                    }
                }
                else
                {
                    k.apply(result, (byte*)dh.getData() + _firstChanOffset, _dt, _nchan, _ntxchan);
                }

                if (tanvecMode)
                {
                    // rotate tangent-space vector data and update main result
                    switch (k.rot)
                    {
                        case 0:  // rot==0 included for completeness, but tanvecMode should be false in this case
                            _result[0] += result[0];
                            _result[1] += result[1];
                            break;
                        case 1:
                            _result[0] -= result[1];
                            _result[1] += result[0];
                            break;
                        case 2:
                            _result[0] -= result[0];
                            _result[1] -= result[1];
                            break;
                        case 3:
                            _result[0] += result[1];
                            _result[1] -= result[0];
                            break;
                    }
                    for (int i = 2; i < _nchan; i++) _result[i] += result[i];
                }
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
        protected BorderMode _uMode, _vMode;  // border modes (clamp,black,periodic)
        protected EdgeFilterMode _efm;        // edge filter mode (rotate when kernel is rotated or not)
    }
}
