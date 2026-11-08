// (not in pbrt-v4) Polarised Fresnel equations. They are FrDielectric() and FrComplex() with the amplitudes kept: where those
// return the average of the two squared amplitudes, these return the reflectance (Fs, Fp) and the phase shift on reflection (Ds,
// Dp) of the perpendicular and parallel polarisation, the terms that PolarisedUPBP's Attenuation.Fresnel() takes (its
// FresnelReal() and FresnelComplex()). The amplitudes and their signs are those of Mitsuba 3 (the Verdet convention of its
// polarization documentation: r_perp and r_parl are pbrt's), so Attenuation.Fresnel() builds Mitsuba's reflection Mueller
// matrix from them: Delta = arg(r_parl) - arg(r_perp) = Dp - Ds, and the block of Mitsuba is [cos, -sin; sin, cos] of Delta.
// PolarisedUPBP's own FresnelReal() has the opposite convention (Ds - Dp is 0 below the Brewster angle, where here it is pi): the
// handedness matrix diag(1, 1, -1, -1) between the two.

using System;
using System.Numerics;

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
            // The polarised equations assume a negative extinction coefficient (eta = n - ik), the opposite of pbrt's
            // convention for FrComplex()
            if (eta.im > 0)
            {
                eta = new pstd.complex(eta.re, -eta.im);
            }

            // Compute complex $\cos\,\theta_\roman{t}$ for Fresnel equations using Snell's law
            Float sin2Theta_i = 1 - Sqr(cosTheta_i);
            pstd.complex sin2Theta_t = sin2Theta_i / Sqr(eta);
            pstd.complex cosTheta_t = pstd.sqrt(1 - sin2Theta_t);
            // Choose the sign of the root that has a nonpositive imaginary part: it fixes the phase difference under total
            // internal reflection (appendix A.2 of "Stellar Polarimetry" by David Clarke, as in Mitsuba 3)
            if (cosTheta_t.im > 0)
            {
                cosTheta_t = new pstd.complex(cosTheta_t.re, -cosTheta_t.im);
            }

            pstd.complex r_parl = (eta * cosTheta_i - cosTheta_t) / (eta * cosTheta_i + cosTheta_t);
            pstd.complex r_perp = (cosTheta_i - eta * cosTheta_t) / (cosTheta_i + eta * cosTheta_t);
            Fs = pstd.norm(r_perp);
            Fp = pstd.norm(r_parl);
            Ds = std.atan2(r_perp.im, r_perp.re);
            Dp = std.atan2(r_parl.im, r_parl.re);
        }

        // (not in pbrt-v4) The reflection of a stack of thin films on a substrate: the amplitude reflection coefficients of the
        // perpendicular and parallel polarisation by the recursion of the Airy formulas (the transfer matrix of the stack for
        // the reflection), returned as FrComplexPolarised() returns them. layerEta and layerThickness (nanometres) list the layers
        // from the ambient medium (real index etaAmbient, cosTheta_i the cosine of the angle in it) to the substrate; the layers
        // have real indices (they do not absorb). The indices are n - ik as in FrComplexPolarised(), so the wave is
        // exp(i (omega t - 2 pi N z / wavelength)) and a layer of thickness d turns a wave that goes through it by exp(-i delta),
        // delta = 2 pi N d cos(theta) / wavelength.
        public static void FrThinFilmPolarised(Float cosTheta_i, Float etaAmbient, Float[] layerEta, Float[] layerThickness,
                                               Float wavelength, pstd.complex etaSubstrate, out Float Fs, out Float Fp,
                                               out Float Ds, out Float Dp)
        {
            int count = layerEta.Length;
            double cosI = Math.Clamp((double)cosTheta_i, 0.0, 1.0);
            double snell = etaAmbient * Math.Sqrt(Math.Max(0.0, 1.0 - cosI * cosI));
            Complex[] index = new Complex[count + 2];
            Complex[] cosines = new Complex[count + 2];
            index[0] = etaAmbient;
            for (int i = 0; i < count; ++i)
            {
                index[i + 1] = layerEta[i];
            }
            index[count + 1] = new Complex(etaSubstrate.re, etaSubstrate.im > 0 ? -etaSubstrate.im : etaSubstrate.im);
            cosines[0] = cosI;
            for (int i = 1; i < count + 2; ++i)
            {
                Complex ratio = snell / index[i];
                Complex cosine = Complex.Sqrt(1.0 - ratio * ratio);
                // The root with a nonpositive imaginary part, as in FrComplexPolarised()
                cosines[i] = cosine.Imaginary > 0 ? Complex.Conjugate(cosine) : cosine;
            }

            Complex reflectionS = Interface(index, cosines, count, true);
            Complex reflectionP = Interface(index, cosines, count, false);
            for (int i = count; i >= 1; --i)
            {
                Complex delta = 2.0 * Math.PI * index[i] * layerThickness[i - 1] * cosines[i] / wavelength;
                Complex phase = Complex.Exp(new Complex(0, -2) * delta);
                Complex above = Interface(index, cosines, i - 1, true);
                reflectionS = (above + reflectionS * phase) / (1.0 + above * reflectionS * phase);
                above = Interface(index, cosines, i - 1, false);
                reflectionP = (above + reflectionP * phase) / (1.0 + above * reflectionP * phase);
            }

            Fs = (Float)(reflectionS.Real * reflectionS.Real + reflectionS.Imaginary * reflectionS.Imaginary);
            Fp = (Float)(reflectionP.Real * reflectionP.Real + reflectionP.Imaginary * reflectionP.Imaginary);
            Ds = (Float)Math.Atan2(reflectionS.Imaginary, reflectionS.Real);
            Dp = (Float)Math.Atan2(reflectionP.Imaginary, reflectionP.Real);
        }

        // The amplitude reflection coefficient of the boundary between medium upper and the one below it
        static Complex Interface(Complex[] index, Complex[] cosines, int upper, bool perpendicular)
        {
            Complex a = index[upper], b = index[upper + 1], ca = cosines[upper], cb = cosines[upper + 1];
            if (perpendicular)
            {
                return (a * ca - b * cb) / (a * ca + b * cb);
            }
            return (b * ca - a * cb) / (b * ca + a * cb);
        }
    }
}
