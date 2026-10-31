// (not in pbrt-v4) mediumevents.cs
//
// The decisions about a medium collision and the ratio-tracking updates that VolPathIntegrator, SimpleVolPathIntegrator, the
// BDPT random walks and the shadow-ray transmittance all take the same way. PolarisedUPBP (Scene/Media.hxx) and
// LuxCoreRender keep them in one place so that the path tracer and the bidirectional tracer cannot drift apart; the
// functions below are the code that was written out in each of those places in pbrt-v4.

namespace pbrt
{

    // MediumEventType Definition
    public enum MediumEventType
    {
        Absorb = 0,
        Scatter = 1,
        Null = 2
    }

    // MediumEvents Definition
    public static unsafe class MediumEvents
    {
        // Chooses the kind of collision at a point of the majorant. The probabilities come from the first wavelength, which
        // is the one that steers the path.
        public static MediumEventType SampleEvent(MediumProperties mp, SampledSpectrum sigma_maj, Float u)
        {
            Float pAbsorb = mp.sigma_a[0] / sigma_maj[0];
            Float pScatter = mp.sigma_s[0] / sigma_maj[0];
            Float pNull = std.max<Float>(0, 1 - pAbsorb - pScatter);
            return (MediumEventType)pbrt.SampleDiscrete(new Float[] { pAbsorb, pScatter, pNull }, u);
        }

        // Ratio-tracking update at a null collision for a transmittance estimate that is not combined with another strategy
        public static void RatioTrackingStep(ref SampledSpectrum Tr, ref SampledSpectrum inv_w, SampledSpectrum T_maj,
                                             SampledSpectrum sigma_maj, SampledSpectrum sigma_n)
        {
            Float pr = T_maj[0] * sigma_maj[0];
            Tr *= T_maj * sigma_n / pr;
            inv_w *= T_maj * sigma_maj / pr;
        }

        // Ratio-tracking update at a null collision for a transmittance that is combined with the light and unidirectional
        // strategies through the rescaled path probabilities r_l and r_u
        public static void RatioTrackingStep(ref SampledSpectrum T_ray, ref SampledSpectrum r_l, ref SampledSpectrum r_u,
                                             SampledSpectrum T_maj, SampledSpectrum sigma_maj, SampledSpectrum sigma_n)
        {
            Float pdf = T_maj[0] * sigma_maj[0];
            T_ray *= T_maj * sigma_n / pdf;
            r_l *= T_maj * sigma_maj / pdf;
            r_u *= T_maj * sigma_n / pdf;
        }

        // The last segment of a ray, from the last collision to the end of the segment
        public static void FinishSegment(ref SampledSpectrum Tr, ref SampledSpectrum inv_w, SampledSpectrum T_maj)
        {
            Tr *= T_maj / T_maj[0];
            inv_w *= T_maj / T_maj[0];
        }

        public static void FinishSegment(ref SampledSpectrum T_ray, ref SampledSpectrum r_l, ref SampledSpectrum r_u,
                                         SampledSpectrum T_maj)
        {
            T_ray *= T_maj / T_maj[0];
            r_l *= T_maj / T_maj[0];
            r_u *= T_maj / T_maj[0];
        }
    }

}  // namespace pbrt
