// PtexFilters.cpp
/*
PTEX SOFTWARE
Copyright 2014 Disney Enterprises, Inc.  All rights reserved
(see LICENSE.txt in ext/ptex)
*/

using System;

namespace Ptex
{
    /** Point-sampling filter for rectangular textures */
    public unsafe class PtexPointFilter : PtexFilter
    {
        public PtexPointFilter(PtexTexture tx) { _tx = tx; }
        public override void release() { }

        public override void eval(float* result, int firstchan, int nchannels,
                                  int faceid, float u, float v,
                                  float uw1, float vw1, float uw2, float vw2,
                                  float width, float blur)
        {
            if (_tx == null || nchannels <= 0) return;
            if (faceid < 0 || faceid >= _tx.numFaces()) return;
            FaceInfo f = _tx.getFaceInfo(faceid);
            int resu = f.res.u(), resv = f.res.v();
            int ui = PtexUtils.clamp((int)(u * (float)resu), 0, resu - 1);
            int vi = PtexUtils.clamp((int)(v * (float)resv), 0, resv - 1);
            _tx.getPixel(faceid, ui, vi, result, firstchan, nchannels);
        }

        PtexTexture _tx;
    }

    /** Point-sampling filter for triangular textures */
    public unsafe class PtexPointFilterTri : PtexFilter
    {
        public PtexPointFilterTri(PtexTexture tx) { _tx = tx; }
        public override void release() { }

        public override void eval(float* result, int firstchan, int nchannels,
                                  int faceid, float u, float v,
                                  float uw1, float vw1, float uw2, float vw2,
                                  float width, float blur)
        {
            if (_tx == null || nchannels <= 0) return;
            if (faceid < 0 || faceid >= _tx.numFaces()) return;
            FaceInfo f = _tx.getFaceInfo(faceid);
            int res = f.res.u();
            int resm1 = res - 1;
            float ut = u * (float)res, vt = v * (float)res;
            int ui = PtexUtils.clamp((int)ut, 0, resm1);
            int vi = PtexUtils.clamp((int)vt, 0, resm1);
            float uf = ut - (float)ui, vf = vt - (float)vi;

            if (uf + vf <= 1.0f)
            {
                // "even" triangles are stored in lower-left half-texture
                _tx.getPixel(faceid, ui, vi, result, firstchan, nchannels);
            }
            else
            {
                // "odd" triangles are stored in upper-right half-texture
                _tx.getPixel(faceid, resm1 - vi, resm1 - ui, result, firstchan, nchannels);
            }
        }

        PtexTexture _tx;
    }

    /** Separable filter with width=4 support.
        The kernel width is calculated as a multiple of 4 times the filter
        width and the texture resolution is chosen such that each kernel
        axis has between 4 and 8.

        For kernel widths too large to handle (because the kernel would
        extend significantly beyond both sides of the face), a special
        Hermite smoothstep is used to interpolate the two nearest 2 samples
        along the affected axis (or axes).
    */
    public unsafe class PtexWidth4Filter : PtexSeparableFilter
    {
        public delegate float KernelFn(float x, float[] c);

        public PtexWidth4Filter(PtexTexture tx, PtexFilter.Options opts, KernelFn k, float[] c = null)
            : base(tx, opts)
        {
            _k = k; _c = c;
        }

        protected override void buildKernel(PtexSeparableKernel k, float u, float v, float uw, float vw,
                                            Res faceRes)
        {
            buildKernelAxis(ref k.res.ulog2, ref k.u, ref k.uw, k.kuA, u, uw, faceRes.ulog2);
            buildKernelAxis(ref k.res.vlog2, ref k.v, ref k.vw, k.kvA, v, vw, faceRes.vlog2);
        }

        float blurFn(float x)
        {
            // 2-unit (x in -1..1) cubic hermite kernel
            // this produces a blur roughly 1.5 times that of the 4-unit b-spline kernel
            x = PtexUtils.abs(x);
            return x < 1.0f ? (2.0f * x - 3.0f) * x * x + 1.0f : 0.0f;
        }

        void buildKernelAxis(ref sbyte k_ureslog2, ref int k_u, ref int k_uw, float[] ku,
                             float u, float uw, int f_ureslog2)
        {
            // build 1 axis (note: "u" labels may repesent either u or v axis)

            // clamp filter width to no smaller than a texel
            uw = PtexUtils.max(uw, PtexUtils.reciprocalPow2(f_ureslog2));

            // compute desired texture res based on filter width
            k_ureslog2 = (sbyte)PtexUtils.calcResFromWidth(uw);
            int resu = 1 << k_ureslog2;
            float uwlo = 1.0f / (float)resu;  // smallest filter width for this res

            // compute lerp weights (amount to blend towards next-lower res)
            float lerp2 = _options.lerp ? (uw - uwlo) / uwlo : 0;
            float lerp1 = 1.0f - lerp2;

            // adjust for large filter widths
            if (uw >= .25f)
            {
                if (uw < .5f)
                {
                    k_ureslog2 = 2;
                    float upix = u * 4.0f - 0.5f;
                    int u1 = (int)(PtexUtils.ceil(upix - 2)), u2 = (int)(PtexUtils.ceil(upix + 2));
                    u1 = u1 & ~1;        // round down to even pair
                    u2 = (u2 + 1) & ~1;  // round up to even pair
                    k_u = u1;
                    k_uw = u2 - u1;
                    float x1 = (float)u1 - upix;
                    for (int i = 0; i < k_uw; i += 2)
                    {
                        float xa = x1 + (float)i, xb = xa + 1.0f, xc = (xa + xb) * 0.25f;
                        // spread the filter gradually to approach the next-lower-res width
                        // at uw = .5, s = 1.0; at uw = 1, s = 0.8
                        float s = 1.0f / (uw + .75f);
                        float ka = _k(xa, _c), kb = _k(xb, _c), kc = blurFn(xc * s);
                        ku[i] = ka * lerp1 + kc * lerp2;
                        ku[i + 1] = kb * lerp1 + kc * lerp2;
                    }
                    return;
                }
                else if (uw < 1)
                {
                    k_ureslog2 = 1;
                    float upix = u * 2.0f - 0.5f;
                    k_u = (int)(PtexUtils.floor(u - .5f)) * 2;
                    k_uw = 4;
                    float x1 = (float)k_u - upix;
                    for (int i = 0; i < k_uw; i += 2)
                    {
                        float xa = x1 + (float)i, xb = xa + 1.0f, xc = (xa + xb) * 0.5f;
                        // spread the filter gradually to approach the next-lower-res width
                        // at uw = .5, s = .8; at uw = 1, s = 0.5
                        float s = 1.0f / (uw * 1.5f + .5f);
                        float ka = blurFn(xa * s), kb = blurFn(xb * s), kc = blurFn(xc * s);
                        ku[i] = ka * lerp1 + kc * lerp2;
                        ku[i + 1] = kb * lerp1 + kc * lerp2;
                    }
                    return;
                }
                else
                {
                    // use res 0 (1 texel per face) w/ no lerping
                    // (future: use face-blended values for filter > 2)
                    k_ureslog2 = 0;
                    float upix = u - .5f;
                    k_uw = 2;
                    float ui = PtexUtils.floor(upix);
                    k_u = (int)ui;
                    ku[0] = blurFn(upix - ui);
                    ku[1] = 1 - ku[0];
                    return;
                }
            }

            // convert from normalized coords to pixel coords
            float upix2 = u * (float)resu - 0.5f;
            float uwpix = uw * (float)resu;

            // find integer pixel extent: [u,v] +/- [2*uw,2*vw]
            // (kernel width is 4 times filter width)
            float dupix = 2.0f * uwpix;
            int u1b = (int)(PtexUtils.ceil(upix2 - dupix)), u2b = (int)(PtexUtils.ceil(upix2 + dupix));

            if (lerp2 != 0)
            {
                // lerp kernel weights towards next-lower res
                // extend kernel width to cover even pairs
                u1b = u1b & ~1;
                u2b = (u2b + 1) & ~1;
                k_u = u1b;
                k_uw = u2b - u1b;

                // compute kernel weights
                float step = 1.0f / uwpix, x1 = ((float)u1b - upix2) * (float)step;
                for (int i = 0; i < k_uw; i += 2)
                {
                    float xa = x1 + (float)i * step, xb = xa + step, xc = (xa + xb) * 0.5f;
                    float ka = _k(xa, _c), kb = _k(xb, _c), kc = _k(xc, _c);
                    ku[i] = ka * lerp1 + kc * lerp2;
                    ku[i + 1] = kb * lerp1 + kc * lerp2;
                }
            }
            else
            {
                k_u = u1b;
                k_uw = u2b - u1b;

                // compute kernel weights
                float x1 = ((float)u1b - upix2) / uwpix, step = 1.0f / uwpix;
                for (int i = 0; i < k_uw; i++) ku[i] = _k(x1 + (float)i * step, _c);
            }
        }

        protected KernelFn _k;     // kernel function
        protected float[] _c;      // kernel coefficients (if any)
    }

    /** Separable bicubic filter */
    public unsafe class PtexBicubicFilter : PtexWidth4Filter
    {
        public PtexBicubicFilter(PtexTexture tx, PtexFilter.Options opts, float sharpness)
            : base(tx, opts, kernelFn, new float[7])
        {
            // compute Cubic filter coefficients:
            // abs(x) < 1:
            //   1/6 * ((12 - 9*B - 6*C)*x^3 + (-18 + 12*B + 6*C)*x^2 + (6 - 2*B))
            //   == c[0]*x^3 + c[1]*x^2 + c[2]
            // abs(x) < 2:
            //   1/6 * ((-B - 6*C)*x^3 + (6*B + 30*C)*x^2 + (-12*B - 48*C)*x + (8*B + 24*C))
            //   == c[3]*x^3 + c[4]*x^2 + c[5]*x + c[6]
            // else: 0
            float B = 1.0f - sharpness;  // choose C = (1-B)/2
            _coeffs = _c;
            _coeffs[0] = 1.5f - B;
            _coeffs[1] = 1.5f * B - 2.5f;
            _coeffs[2] = 1.0f - (float)(1.0 / 3.0) * B;
            _coeffs[3] = (float)(1.0 / 3.0) * B - 0.5f;
            _coeffs[4] = 2.5f - 1.5f * B;
            _coeffs[5] = 2.0f * B - 4.0f;
            _coeffs[6] = 2.0f - (float)(2.0 / 3.0) * B;
        }

        static float kernelFn(float x, float[] c)
        {
            x = PtexUtils.abs(x);
            if (x < 1.0f) return (c[0] * x + c[1]) * x * x + c[2];
            else if (x < 2.0f) return ((c[3] * x + c[4]) * x + c[5]) * x + c[6];
            else return 0.0f;
        }

        float[] _coeffs;  // filter coefficients for current sharpness
    }

    /** Separable gaussian filter */
    public unsafe class PtexGaussianFilter : PtexWidth4Filter
    {
        public PtexGaussianFilter(PtexTexture tx, PtexFilter.Options opts)
            : base(tx, opts, kernelFn) { }

        static float kernelFn(float x, float[] c)
        {
            return (float)MathF.Exp(-2.0f * x * x);
        }
    }

    /** Rectangular box filter.

        The box is convolved with the texels as area samples and thus the kernel function is
        actually trapezoidally shaped.
     */
    public unsafe class PtexBoxFilter : PtexSeparableFilter
    {
        public PtexBoxFilter(PtexTexture tx, PtexFilter.Options opts)
            : base(tx, opts) { }

        protected override void buildKernel(PtexSeparableKernel k, float u, float v, float uw, float vw,
                                            Res faceRes)
        {
            // clamp filter width to no larger than 1.0
            uw = PtexUtils.min(uw, 1.0f);
            vw = PtexUtils.min(vw, 1.0f);

            // clamp filter width to no smaller than a texel
            uw = PtexUtils.max(uw, PtexUtils.reciprocalPow2(faceRes.ulog2));
            vw = PtexUtils.max(vw, PtexUtils.reciprocalPow2(faceRes.vlog2));

            // compute desired texture res based on filter width
            byte ureslog2 = (byte)PtexUtils.calcResFromWidth(uw);
            byte vreslog2 = (byte)PtexUtils.calcResFromWidth(vw);
            Res res = new Res((sbyte)ureslog2, (sbyte)vreslog2);
            k.res = res;

            // convert from normalized coords to pixel coords
            u = u * (float)k.res.u();
            v = v * (float)k.res.v();
            uw *= (float)k.res.u();
            vw *= (float)k.res.v();

            // find integer pixel extent: [u,v] +/- [uw/2,vw/2]
            // (box is 1 unit wide for a 1 unit filter period)
            float u1 = u - 0.5f * uw, u2 = u + 0.5f * uw;
            float v1 = v - 0.5f * vw, v2 = v + 0.5f * vw;
            float u1floor = PtexUtils.floor(u1), u2ceil = PtexUtils.ceil(u2);
            float v1floor = PtexUtils.floor(v1), v2ceil = PtexUtils.ceil(v2);
            k.u = (int)u1floor;
            k.v = (int)v1floor;
            k.uw = (int)u2ceil - k.u;
            k.vw = (int)v2ceil - k.v;

            // compute kernel weights along u and v directions
            computeWeights(k.kuA, k.uw, 1.0f - (u1 - u1floor), 1.0f - (u2ceil - u2));
            computeWeights(k.kvA, k.vw, 1.0f - (v1 - v1floor), 1.0f - (v2ceil - v2));
        }

        void computeWeights(float[] kernel, int size, float f1, float f2)
        {
            System.Diagnostics.Debug.Assert(size >= 1 && size <= 3);
            if (size == 1)
            {
                kernel[0] = f1 + f2 - 1.0f;
            }
            else
            {
                kernel[0] = f1;
                for (int i = 1; i < size - 1; i++) kernel[i] = 1.0f;
                kernel[size - 1] = f2;
            }
        }
    }

    /** Bilinear filter (for rectangular textures) */
    public unsafe class PtexBilinearFilter : PtexSeparableFilter
    {
        public PtexBilinearFilter(PtexTexture tx, PtexFilter.Options opts)
            : base(tx, opts) { }

        protected override void buildKernel(PtexSeparableKernel k, float u, float v, float uw, float vw,
                                            Res faceRes)
        {
            // clamp filter width to no larger than 1.0
            uw = PtexUtils.min(uw, 1.0f);
            vw = PtexUtils.min(vw, 1.0f);

            // clamp filter width to no smaller than a texel
            uw = PtexUtils.max(uw, PtexUtils.reciprocalPow2(faceRes.ulog2));
            vw = PtexUtils.max(vw, PtexUtils.reciprocalPow2(faceRes.vlog2));

            byte ureslog2 = (byte)PtexUtils.calcResFromWidth(uw);
            byte vreslog2 = (byte)PtexUtils.calcResFromWidth(vw);
            Res res = new Res((sbyte)ureslog2, (sbyte)vreslog2);
            k.res = res;

            // convert from normalized coords to pixel coords
            float upix = u * (float)k.res.u() - 0.5f;
            float vpix = v * (float)k.res.v() - 0.5f;
            float ufloor = PtexUtils.floor(upix);
            float vfloor = PtexUtils.floor(vpix);
            k.u = (int)ufloor;
            k.v = (int)vfloor;
            k.uw = 2;
            k.vw = 2;

            // compute kernel weights
            float ufrac = upix - ufloor, vfrac = vpix - vfloor;
            k.kuA[0] = 1.0f - ufrac;
            k.kuA[1] = ufrac;
            k.kvA[0] = 1.0f - vfrac;
            k.kvA[1] = vfrac;
        }
    }

    public abstract unsafe partial class PtexFilter
    {
        // Get a filter for the given texture and options. Returns null for an unknown mesh type.
        public static PtexFilter getFilter(PtexTexture tex, PtexFilter.Options opts)
        {
            switch (tex.meshType())
            {
                case MeshType.mt_quad:
                    switch (opts.filter)
                    {
                        case FilterType.f_point: return new PtexPointFilter(tex);
                        case FilterType.f_bilinear: return new PtexBilinearFilter(tex, opts);
                        default:
                        case FilterType.f_box: return new PtexBoxFilter(tex, opts);
                        case FilterType.f_gaussian: return new PtexGaussianFilter(tex, opts);
                        case FilterType.f_bicubic: return new PtexBicubicFilter(tex, opts, opts.sharpness);
                        case FilterType.f_bspline: return new PtexBicubicFilter(tex, opts, 0.0f);
                        case FilterType.f_catmullrom: return new PtexBicubicFilter(tex, opts, 1.0f);
                        case FilterType.f_mitchell: return new PtexBicubicFilter(tex, opts, 2.0f / 3.0f);
                    }

                case MeshType.mt_triangle:
                    switch (opts.filter)
                    {
                        case FilterType.f_point: return new PtexPointFilterTri(tex);
                        default: return new PtexTriangleFilter(tex, opts);
                    }
            }
            return null;
        }
    }
}
