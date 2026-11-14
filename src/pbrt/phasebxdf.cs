// (not in pbrt-v4) phasebxdf.cs
//
// A phase function seen as a BxDF, so that a scattering event in a medium and one at a surface go through the same BSDF type
// (Fong et al., "Production Volume Rendering", section 3.1; PolarisedUPBP's BSDF_LS, whose kScatter event covers both).
// A phase function has no shading frame: the directions are used as they are, in render space.

namespace pbrt
{

    // PhaseBxDF Definition
    public sealed unsafe class PhaseBxDF : BxDF
    {
        // PhaseBxDF Public Methods
        public PhaseBxDF(PhaseFunction phase)
        {
            this.phase = phase;
        }

        public override BxDFFlags Flags()
        {
            return BxDFFlags.Reflection | BxDFFlags.Transmission | BxDFFlags.Glossy;
        }

        public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            if (phase.WavelengthDependent())
                return phase.pSpectral(wo, wi);
            return new SampledSpectrum(phase.p(wo, wi));
        }

        public override pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode,
                                                           BxDFReflTransFlags sampleFlags)
        {
            pstd.optional<PhaseFunctionSample> ps = phase.Sample_p(wo, u);
            if (!ps)
                return default;
            if (ps.value().delta)
                return new BSDFSample(ps.value().hasSpec ? ps.value().spec : new SampledSpectrum(ps.value().p), ps.value().wi, ps.value().pdf,
                                      BxDFFlags.SpecularTransmission);
            if (ps.value().hasSpec)
                return new BSDFSample(ps.value().spec, ps.value().wi, ps.value().pdf, Flags());
            if (phase.WavelengthDependent())
                return new BSDFSample(phase.pSpectral(wo, ps.value().wi), ps.value().wi, ps.value().pdf, Flags());
            return new BSDFSample(new SampledSpectrum(ps.value().p), ps.value().wi, ps.value().pdf, Flags());
        }

        public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            return phase.PDF(wo, wi);
        }

        public override void Regularize() { }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ PhaseBxDF phase: %s ]", phase);
        }

        // PhaseBxDF Private Members
        private PhaseFunction phase;
    }

}  // namespace pbrt
