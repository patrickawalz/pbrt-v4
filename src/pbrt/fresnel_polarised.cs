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
            FrThinFilmPolarised(cosTheta_i, etaAmbient, layerEta, layerThickness, false, wavelength, etaSubstrate, out Fs, out Fp,
                                out Ds, out Dp, out Float _, out Float _);
        }

        // The same with the layers listed from the substrate to the ambient medium when `reversed` (the light arrives from
        // the other side of the stack), and with the phases of the amplitude transmission coefficients of the two
        // polarisations (DsT, DpT). The layers do not absorb, so the transmittances are 1 - Fs and 1 - Fp, and the phase of a
        // transmission is the same in both directions. Nothing is allocated: this is called for every microfacet of a rough
        // film, at every wavelength.
        public static void FrThinFilmPolarised(Float cosTheta_i, Float etaAmbient, Float[] layerEta, Float[] layerThickness,
                                               bool reversed, Float wavelength, pstd.complex etaSubstrate, out Float Fs,
                                               out Float Fp, out Float Ds, out Float Dp, out Float DsT, out Float DpT)
        {
            int count = layerEta.Length;
            double cosI = Math.Clamp((double)cosTheta_i, 0.0, 1.0);
            double snell = etaAmbient * Math.Sqrt(Math.Max(0.0, 1.0 - cosI * cosI));
            Complex* index = stackalloc Complex[count + 2];
            Complex* cosines = stackalloc Complex[count + 2];
            index[0] = etaAmbient;
            for (int i = 0; i < count; ++i)
            {
                index[i + 1] = layerEta[reversed ? count - 1 - i : i];
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
            Complex transmissionS = InterfaceTransmission(index, cosines, count, true);
            Complex transmissionP = InterfaceTransmission(index, cosines, count, false);
            for (int i = count; i >= 1; --i)
            {
                Complex delta = 2.0 * Math.PI * index[i] * layerThickness[reversed ? count - i : i - 1] * cosines[i] / wavelength;
                Complex across = Complex.Exp(new Complex(0, -1) * delta);
                Complex phase = across * across;
                Complex above = Interface(index, cosines, i - 1, true);
                Complex denominator = 1.0 + above * reflectionS * phase;
                transmissionS = InterfaceTransmission(index, cosines, i - 1, true) * transmissionS * across / denominator;
                reflectionS = (above + reflectionS * phase) / denominator;
                above = Interface(index, cosines, i - 1, false);
                denominator = 1.0 + above * reflectionP * phase;
                transmissionP = InterfaceTransmission(index, cosines, i - 1, false) * transmissionP * across / denominator;
                reflectionP = (above + reflectionP * phase) / denominator;
            }

            Fs = (Float)(reflectionS.Real * reflectionS.Real + reflectionS.Imaginary * reflectionS.Imaginary);
            Fp = (Float)(reflectionP.Real * reflectionP.Real + reflectionP.Imaginary * reflectionP.Imaginary);
            Ds = (Float)Math.Atan2(reflectionS.Imaginary, reflectionS.Real);
            Dp = (Float)Math.Atan2(reflectionP.Imaginary, reflectionP.Real);
            DsT = (Float)Math.Atan2(transmissionS.Imaginary, transmissionS.Real);
            DpT = (Float)Math.Atan2(transmissionP.Imaginary, transmissionP.Real);
        }

        // (not in pbrt-v4) The reflection of a Bragg mirror: the layers of `layerEta` and `layerThickness` are one cell, and the
        // stack is `repeat` such cells, one after the other (a one-dimensional photonic crystal), between the ambient medium and
        // the substrate. Everything else is as in FrThinFilmPolarised(), and with one cell the two are the same.
        //
        // The cost does not grow with the number of cells. Each field is a wave that goes down and one that goes up; the
        // amplitudes above a boundary are those below it times (1 / t) [1 r; r 1], and those at the top of a layer are those at
        // its bottom times diag(exp(i delta), exp(-i delta)). The matrix C of one cell (its layers, each with the boundary to
        // the next one, the last one with the boundary back to the first) has determinant one, and the power of such a matrix
        // is known (the identity of Chebyshev, the theorem of Abeles):
        //   C^N = U_{N-1}(a) C - U_{N-2}(a) I,   a = trace(C) / 2 = cos(K Lambda),   U_n(cos x) = sin((n + 1) x) / sin x
        // K is the Bloch wavenumber of the crystal; where |a| > 1 it is complex, the sines grow as hyperbolic ones and the
        // mirror reflects nearly everything: a photonic band gap. This is the closed form of Yeh ("Optical Waves in Layered
        // Media", 1988) that Fourneau, Pacanowski and Barla, "Interactive Exploration of Vivid Material Iridescence using Bragg
        // Mirrors" (2024) use for a cell of two films in a host of the index of the first one; here for any cell, on any
        // substrate, and with the phases.
        public static void FrBraggPolarised(Float cosTheta_i, Float etaAmbient, Float[] layerEta, Float[] layerThickness, int repeat,
                                            bool reversed, Float wavelength, pstd.complex etaSubstrate, out Float Fs,
                                            out Float Fp, out Float Ds, out Float Dp, out Float DsT, out Float DpT)
        {
            FrBraggPolarised(cosTheta_i, etaAmbient, layerEta, null, layerThickness, repeat, reversed, wavelength, etaSubstrate, out Fs, out Fp, out Ds, out Dp,
                             out DsT, out DpT, out Float _, out Float _);
        }

        // The same with films that absorb: `layerK` (null: none) has their extinction coefficients, the index of a film being
        // n - i k. Ts and Tp are the transmittances, the power that reaches the substrate: 1 - Fs and 1 - Fp without absorption,
        // less with it.
        public static void FrBraggPolarised(Float cosTheta_i, Float etaAmbient, Float[] layerEta, Float[] layerK, Float[] layerThickness, int repeat,
                                            bool reversed, Float wavelength, pstd.complex etaSubstrate, out Float Fs,
                                            out Float Fp, out Float Ds, out Float Dp, out Float DsT, out Float DpT, out Float Ts, out Float Tp)
        {
            int count = layerEta.Length;
            double cosI = Math.Clamp((double)cosTheta_i, 0.0, 1.0);
            double snell = etaAmbient * Math.Sqrt(Math.Max(0.0, 1.0 - cosI * cosI));
            Complex* index = stackalloc Complex[count + 2];
            Complex* cosines = stackalloc Complex[count + 2];
            Complex* down = stackalloc Complex[count + 2];
            Complex* up = stackalloc Complex[count + 2];
            index[0] = etaAmbient;
            for (int i = 0; i < count; ++i)
            {
                int layer = reversed ? count - 1 - i : i;
                index[i + 1] = layerK != null ? new Complex(layerEta[layer], -Math.Abs(layerK[layer])) : layerEta[layer];
            }
            index[count + 1] = new Complex(etaSubstrate.re, etaSubstrate.im > 0 ? -etaSubstrate.im : etaSubstrate.im);
            cosines[0] = cosI;
            for (int i = 1; i < count + 2; ++i)
            {
                Complex ratio = snell / index[i];
                Complex cosine = Complex.Sqrt(1.0 - ratio * ratio);
                cosines[i] = cosine.Imaginary > 0 ? Complex.Conjugate(cosine) : cosine;
            }
            Complex imaginary = new Complex(0, 1);
            for (int i = 1; i <= count; ++i)
            {
                // (what a layer does to the wave that goes down and to the one that goes up, the same for both polarisations)
                Complex turn = 2.0 * Math.PI * index[i] * layerThickness[reversed ? count - i : i - 1] * cosines[i] / wavelength;
                down[i] = Complex.Exp(imaginary * turn);
                up[i] = 1.0 / down[i];
            }
            Complex reflectionS, reflectionP, transmissionS, transmissionP;
            BraggAmplitudes(index, cosines, down, up, count, repeat, true, out reflectionS, out transmissionS);
            BraggAmplitudes(index, cosines, down, up, count, repeat, false, out reflectionP, out transmissionP);
            Fs = (Float)(reflectionS.Real * reflectionS.Real + reflectionS.Imaginary * reflectionS.Imaginary);
            Fp = (Float)(reflectionP.Real * reflectionP.Real + reflectionP.Imaginary * reflectionP.Imaginary);
            Ds = (Float)Math.Atan2(reflectionS.Imaginary, reflectionS.Real);
            Dp = (Float)Math.Atan2(reflectionP.Imaginary, reflectionP.Real);
            DsT = (Float)Math.Atan2(transmissionS.Imaginary, transmissionS.Real);
            DpT = (Float)Math.Atan2(transmissionP.Imaginary, transmissionP.Real);
            // The power that goes into the substrate, by the flux across the boundary: Re(n cos) for the perpendicular
            // polarisation and Re(n conj(cos)) for the parallel one, over those of the ambient medium
            double incident = etaAmbient * cosI;
            if (incident > 0)
            {
                Complex last = index[count + 1], lastCosine = cosines[count + 1];
                double magnitudeS = transmissionS.Real * transmissionS.Real + transmissionS.Imaginary * transmissionS.Imaginary;
                double magnitudeP = transmissionP.Real * transmissionP.Real + transmissionP.Imaginary * transmissionP.Imaginary;
                Ts = (Float)Math.Clamp(magnitudeS * Math.Max(0.0, (last * lastCosine).Real) / incident, 0.0, 1.0);
                Tp = (Float)Math.Clamp(magnitudeP * Math.Max(0.0, (last * Complex.Conjugate(lastCosine)).Real) / incident, 0.0, 1.0);
            }
            else
            {
                Ts = 0;
                Tp = 0;
            }
        }

        // The amplitude reflection and transmission coefficients of `repeat` cells for one polarisation
        static void BraggAmplitudes(Complex* index, Complex* cosines, Complex* down, Complex* up, int count, int repeat, bool perpendicular,
                                    out Complex reflection, out Complex transmission)
        {
            Complex i = new Complex(0, 1);
            // The matrix of one cell
            Complex c11 = 1, c12 = 0, c21 = 0, c22 = 1;
            for (int layer = 1; layer <= count; ++layer)
            {
                int next = layer == count ? 1 : layer + 1;
                Complex r, t;
                BoundaryAmplitudes(index[layer], index[next], cosines[layer], cosines[next], perpendicular, out r, out t);
                // (the layer, then its boundary: diag(e, 1 / e) (1 / t) [1 r; r 1])
                Complex m11 = down[layer] / t, m22 = up[layer] / t;
                Complex m12 = m11 * r, m21 = m22 * r;
                Complex n11 = c11 * m11 + c12 * m21, n12 = c11 * m12 + c12 * m22, n21 = c21 * m11 + c22 * m21, n22 = c21 * m12 + c22 * m22;
                c11 = n11;
                c12 = n12;
                c21 = n21;
                c22 = n22;
            }

            // Its power, times exp(-N |Im K Lambda|): inside a band gap the power itself would overflow, the reflection does not
            // depend on the factor, and the transmission is given it back
            Complex p11 = c11, p12 = c12, p21 = c21, p22 = c22;
            double scale = 1;
            if (repeat > 1)
            {
                Complex half = 0.5 * (c11 + c22);
                Complex angle = Complex.Acos(half);
                Complex sine = Complex.Sin(angle);
                Complex last, beforeLast;
                double logScale = 0;
                if (Complex.Abs(sine) < 1e-9)
                {
                    // (the edge of a band gap: U_n(1) = n + 1, U_n(-1) = (n + 1) (-1)^n)
                    double sign = half.Real < 0 ? -1 : 1;
                    last = repeat * ((repeat - 1) % 2 == 0 ? 1 : sign);
                    beforeLast = (repeat - 1) * ((repeat - 2) % 2 == 0 ? 1 : sign);
                }
                else
                {
                    logScale = repeat * Math.Abs(angle.Imaginary);
                    last = (Complex.Exp(i * angle * repeat - logScale) - Complex.Exp(-i * angle * repeat - logScale)) / (2.0 * i * sine);
                    beforeLast = (Complex.Exp(i * angle * (repeat - 1) - logScale) - Complex.Exp(-i * angle * (repeat - 1) - logScale)) / (2.0 * i * sine);
                }
                scale = Math.Exp(-logScale);
                p11 = last * c11 - beforeLast;
                p12 = last * c12;
                p21 = last * c21;
                p22 = last * c22 - beforeLast;
            }

            // Between the ambient medium and the substrate: the boundary into the first layer, the cells, and the boundary from
            // the first layer to the substrate (the last boundary of the last cell leads back into the first layer)
            Complex rTop, tTop, rBottom, tBottom;
            BoundaryAmplitudes(index[0], index[1], cosines[0], cosines[1], perpendicular, out rTop, out tTop);
            BoundaryAmplitudes(index[1], index[count + 1], cosines[1], cosines[count + 1], perpendicular, out rBottom, out tBottom);
            // (the first column of [1 rTop; rTop 1] P [1 rBottom; rBottom 1], over tTop tBottom)
            Complex q11 = p11 + p12 * rBottom, q21 = p21 + p22 * rBottom;
            Complex t11 = (q11 + rTop * q21) / (tTop * tBottom), t21 = (rTop * q11 + q21) / (tTop * tBottom);
            reflection = t21 / t11;
            transmission = scale / t11;
        }

        // The amplitude reflection and transmission coefficients of the boundary from the medium a into the medium b
        static void BoundaryAmplitudes(Complex a, Complex b, Complex ca, Complex cb, bool perpendicular, out Complex r, out Complex t)
        {
            if (perpendicular)
            {
                r = (a * ca - b * cb) / (a * ca + b * cb);
                t = 2.0 * a * ca / (a * ca + b * cb);
            }
            else
            {
                r = (b * ca - a * cb) / (b * ca + a * cb);
                t = 2.0 * a * ca / (b * ca + a * cb);
            }
        }

        // The amplitude reflection coefficient of the boundary between medium upper and the one below it
        static Complex Interface(Complex* index, Complex* cosines, int upper, bool perpendicular)
        {
            Complex a = index[upper], b = index[upper + 1], ca = cosines[upper], cb = cosines[upper + 1];
            if (perpendicular)
            {
                return (a * ca - b * cb) / (a * ca + b * cb);
            }
            return (b * ca - a * cb) / (b * ca + a * cb);
        }

        // The amplitude transmission coefficient of the same boundary
        static Complex InterfaceTransmission(Complex* index, Complex* cosines, int upper, bool perpendicular)
        {
            Complex a = index[upper], b = index[upper + 1], ca = cosines[upper], cb = cosines[upper + 1];
            if (perpendicular)
            {
                return 2.0 * a * ca / (a * ca + b * cb);
            }
            return 2.0 * a * ca / (b * ca + a * cb);
        }
    }
}
