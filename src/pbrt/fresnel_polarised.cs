// (not in pbrt-v4) Polarised Fresnel equations. They are FrDielectric() and FrComplex() with the amplitudes kept: where those
// return the average of the two squared amplitudes, these return the reflectance (Fs, Fp) and the phase shift on reflection (Ds,
// Dp) of the perpendicular and parallel polarisation, the terms that PolarisedUPBP's Attenuation.Fresnel() takes (its
// FresnelReal() and FresnelComplex()). The amplitudes are those of Mitsuba 3's fresnel_polarized(). The parallel amplitude is
// taken with the sign of the Stokes frame of PolarisedUPBP, in which the two amplitudes have the same sign at normal incidence.

using System;

namespace pbrt
{
    public static unsafe partial class pbrt
    {
        // Fresnel Inline Functions
        public static void FrDielectricPolarised(Float cosTheta_i, Float eta, out Float Fs, out Float Fp, out Float Ds,
                                                 out Float Dp)
        {
            cosTheta_i = Clamp(cosTheta_i, -1, 1);
            // Potentially flip interface orientation for Fresnel equations
            if (cosTheta_i < 0)
            {
                eta = 1 / eta;
                cosTheta_i = -cosTheta_i;
            }

            FrComplexPolarised(cosTheta_i, new pstd.complex(eta), out Fs, out Fp, out Ds, out Dp);
        }

        public static void FrComplexPolarised(Float cosTheta_i, pstd.complex eta, out Float Fs, out Float Fp, out Float Ds,
                                              out Float Dp)
        {
            cosTheta_i = Clamp(cosTheta_i, 0, 1);
            // Compute complex $\cos\,\theta_\roman{t}$ for Fresnel equations using Snell's law
            Float sin2Theta_i = 1 - Sqr(cosTheta_i);
            pstd.complex sin2Theta_t = sin2Theta_i / Sqr(eta);
            pstd.complex cosTheta_t = pstd.sqrt(1 - sin2Theta_t);

            pstd.complex r_parl = (eta * cosTheta_i - cosTheta_t) / (eta * cosTheta_i + cosTheta_t);
            pstd.complex r_perp = (cosTheta_i - eta * cosTheta_t) / (cosTheta_i + eta * cosTheta_t);
            Fs = pstd.norm(r_perp);
            Fp = pstd.norm(r_parl);
            Ds = std.atan2(r_perp.im, r_perp.re);
            Dp = std.atan2(-r_parl.im, -r_parl.re);
        }
    }
}
