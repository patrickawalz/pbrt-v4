// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// bsdf.h, bsdf.cpp

using System;

namespace pbrt
{

    // BSDF Definition
    public unsafe struct BSDF
    {
        // BSDF Public Methods
        public BSDF(Normal3f ns, Vector3f dpdus, BxDF bxdf)
        {
            this.bxdf = bxdf;
            this.shadingFrame = Frame.FromXZ(pbrt.Normalize(dpdus), (Vector3f)ns);
        }

        // (not in pbrt-v4) The BSDF of a scattering event in a medium: the phase function in the place of the BxDF and no shading
        // frame, so the directions are not tested against a surface (a direction in the plane of a frame is not special in a medium).
        public BSDF(PhaseFunction phase)
        {
            this.bxdf = new PhaseBxDF(phase);
            this.shadingFrame = Frame.FromXZ(new Vector3f(1, 0, 0), new Vector3f(0, 0, 1));
            this.isVolume = true;
        }

        public static implicit operator bool(BSDF b) { return b.bxdf != null; }

        public BxDFFlags Flags() { return bxdf.Flags(); }

        // (not in pbrt-v4) For the polarised integrator
        public BxDF GetBxDF() { return bxdf; }

        public Vector3f RenderToLocal(Vector3f v) { return shadingFrame.ToLocal(v); }
        public Vector3f LocalToRender(Vector3f v) { return shadingFrame.FromLocal(v); }

        public SampledSpectrum f(Vector3f woRender, Vector3f wiRender, TransportMode mode)
        {
            Vector3f wi = RenderToLocal(wiRender), wo = RenderToLocal(woRender);
            if (wo.z == 0 && !isVolume)
                return default;
            return bxdf.f(wo, wi, mode);
        }
        public SampledSpectrum f(Vector3f woRender, Vector3f wiRender)
        {
            return f(woRender, wiRender, TransportMode.Radiance);
        }

        public pstd.optional<BSDFSample> Sample_f(Vector3f woRender, Float u, Point2f u2, TransportMode mode,
                                                  BxDFReflTransFlags sampleFlags)
        {
            Vector3f wo = RenderToLocal(woRender);
            if ((wo.z == 0 && !isVolume) || (bxdf.Flags() & sampleFlags) == 0)
                return default;
            // Sample _bxdf_ and return _BSDFSample_
            pstd.optional<BSDFSample> bs = bxdf.Sample_f(wo, u, u2, mode, sampleFlags);
            if (bs)
                pbrt.DCHECK_GE(bs.value().pdf, 0f);
            if (!bs || !(bool)bs.value().f || bs.value().pdf == 0 || (bs.value().wi.z == 0 && !isVolume))
                return default;
            BSDFSample s = bs.value();
            s.wi = LocalToRender(s.wi);
            return s;
        }
        public pstd.optional<BSDFSample> Sample_f(Vector3f woRender, Float u, Point2f u2)
        {
            return Sample_f(woRender, u, u2, TransportMode.Radiance, BxDFReflTransFlags.All);
        }

        public Float PDF(Vector3f woRender, Vector3f wiRender, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            Vector3f wo = RenderToLocal(woRender), wi = RenderToLocal(wiRender);
            if (wo.z == 0 && !isVolume)
                return 0;
            return bxdf.PDF(wo, wi, mode, sampleFlags);
        }
        public Float PDF(Vector3f woRender, Vector3f wiRender)
        {
            return PDF(woRender, wiRender, TransportMode.Radiance, BxDFReflTransFlags.All);
        }

        public SampledSpectrum rho(pstd.span<Point2f> u1, pstd.span<Float> uc, pstd.span<Point2f> u2)
        {
            return bxdf.rho(u1, uc, u2);
        }
        public SampledSpectrum rho(Vector3f woRender, pstd.span<Float> uc, pstd.span<Point2f> u)
        {
            Vector3f wo = RenderToLocal(woRender);
            return bxdf.rho(wo, uc, u);
        }

        public void Regularize() { bxdf.Regularize(); }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ BSDF bxdf: %s shadingFrame: %s ]", bxdf, shadingFrame);
        }

        // BSDF Private Members
        private BxDF bxdf;
        private Frame shadingFrame;
        private bool isVolume;
    }

}  // namespace pbrt
