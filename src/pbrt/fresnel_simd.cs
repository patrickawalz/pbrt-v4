// (not in pbrt-v4) fresnel_simd.cs: the Fresnel equations at the four wavelengths of a sample at once.
//
// FrComplex() and FrComplexPolarised() for a conductor's index (eta, k) at every wavelength are the code of the functions
// for one wavelength (util/scattering.cs, fresnel_polarised.cs) on a complex number whose two parts are SIMD registers
// (SpectrumComplex has the operators of pstd.complex, operation for operation), so each value is that of the function for
// one wavelength to the last bit. The phases (an arc tangent each) are still taken one at a time.
//
// FrThinFilmPolarised() for the four wavelengths is the recursion of the function for one wavelength in double
// precision, on four doubles at a time. That function works with System.Numerics.Complex, whose division, root and
// exponential are other algorithms than the plain formulas here, so the values agree to rounding (about 1e-12), not to
// the last bit.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
#if PBRT_FLOAT_AS_DOUBLE
using SpectrumVector = System.Runtime.Intrinsics.Vector256<double>;
using SpectrumVectors = System.Runtime.Intrinsics.Vector256;
#else
using SpectrumVector = System.Runtime.Intrinsics.Vector128<float>;
using SpectrumVectors = System.Runtime.Intrinsics.Vector128;
#endif

namespace pbrt
{
    // A complex number at each of the four wavelengths: pstd.complex with a register for each part
    internal struct SpectrumComplex
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SpectrumComplex(SpectrumVector re)
        {
            this.re = re;
            im = SpectrumVector.Zero;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SpectrumComplex(SpectrumVector re, SpectrumVector im)
        {
            this.re = re;
            this.im = im;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SpectrumComplex operator +(SpectrumComplex a, SpectrumComplex z)
        {
            return new SpectrumComplex(a.re + z.re, a.im + z.im);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SpectrumComplex operator -(SpectrumComplex a, SpectrumComplex z)
        {
            return new SpectrumComplex(a.re - z.re, a.im - z.im);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SpectrumComplex operator *(SpectrumComplex a, SpectrumComplex z)
        {
            return new SpectrumComplex(a.re * z.re - a.im * z.im, a.re * z.im + a.im * z.re);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SpectrumComplex operator /(SpectrumComplex a, SpectrumComplex z)
        {
            SpectrumVector scale = SpectrumVector.One / (z.re * z.re + z.im * z.im);
            return new SpectrumComplex(scale * (a.re * z.re + a.im * z.im), scale * (a.im * z.re - a.re * z.im));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SpectrumVector Norm()
        {
            return re * re + im * im;
        }

        // pstd.sqrt(complex)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SpectrumComplex Sqrt(SpectrumComplex z)
        {
            SpectrumVector half = SpectrumVectors.Create((Float)0.5);
            SpectrumVector n = SpectrumVectors.Sqrt(z.Norm());
            SpectrumVector t1 = SpectrumVectors.Sqrt(half * (n + SpectrumVectors.Abs(z.re)));
            SpectrumVector t2 = half * z.im / t1;
            SpectrumVector positive = SpectrumVectors.GreaterThanOrEqual(z.re, SpectrumVector.Zero);
            SpectrumVector re = SpectrumVectors.ConditionalSelect(positive, t1, SpectrumVectors.Abs(t2));
            SpectrumVector im = SpectrumVectors.ConditionalSelect(positive, t2, SpectrumVectors.CopySign(t1, z.im));
            SpectrumVector none = SpectrumVectors.Equals(n, SpectrumVector.Zero);
            return new SpectrumComplex(SpectrumVectors.ConditionalSelect(none, SpectrumVector.Zero, re), SpectrumVectors.ConditionalSelect(none, SpectrumVector.Zero, im));
        }

        public SpectrumVector re, im;
    }

    // A complex number in double precision at each of the four wavelengths
    internal struct WideComplex
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public WideComplex(Vector256<double> re)
        {
            this.re = re;
            im = Vector256<double>.Zero;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public WideComplex(Vector256<double> re, Vector256<double> im)
        {
            this.re = re;
            this.im = im;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static WideComplex operator +(WideComplex a, WideComplex z)
        {
            return new WideComplex(a.re + z.re, a.im + z.im);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static WideComplex operator -(WideComplex a, WideComplex z)
        {
            return new WideComplex(a.re - z.re, a.im - z.im);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static WideComplex operator *(WideComplex a, WideComplex z)
        {
            return new WideComplex(a.re * z.re - a.im * z.im, a.re * z.im + a.im * z.re);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static WideComplex operator *(WideComplex a, Vector256<double> s)
        {
            return new WideComplex(a.re * s, a.im * s);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static WideComplex operator /(WideComplex a, WideComplex z)
        {
            Vector256<double> scale = Vector256<double>.One / (z.re * z.re + z.im * z.im);
            return new WideComplex(scale * (a.re * z.re + a.im * z.im), scale * (a.im * z.re - a.re * z.im));
        }

        // The root with a nonpositive imaginary part (the other one where the principal root has a positive one)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static WideComplex SqrtBelow(WideComplex z)
        {
            Vector256<double> half = Vector256.Create(0.5);
            Vector256<double> n = Vector256.Sqrt(z.re * z.re + z.im * z.im);
            Vector256<double> t1 = Vector256.Sqrt(half * (n + Vector256.Abs(z.re)));
            Vector256<double> t2 = half * z.im / t1;
            Vector256<double> positive = Vector256.GreaterThanOrEqual(z.re, Vector256<double>.Zero);
            Vector256<double> re = Vector256.ConditionalSelect(positive, t1, Vector256.Abs(t2));
            // (the imaginary part of the principal root has the sign of z.im; the root wanted has it nonpositive)
            Vector256<double> im = -Vector256.Abs(Vector256.ConditionalSelect(positive, t2, t1));
            Vector256<double> none = Vector256.Equals(n, Vector256<double>.Zero);
            return new WideComplex(Vector256.ConditionalSelect(none, Vector256<double>.Zero, re), Vector256.ConditionalSelect(none, Vector256<double>.Zero, im));
        }

        // exp(-i z)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static WideComplex ExpMinusI(WideComplex z)
        {
            Vector256<double> size = Vector256.Exp(z.im);
            return new WideComplex(size * Vector256.Cos(z.re), -(size * Vector256.Sin(z.re)));
        }

        public Vector256<double> re, im;
    }

    public static unsafe partial class pbrt
    {
        // FrComplex() of util/scattering.cs for the index eta + i k of every wavelength
        public static SampledSpectrum FrComplexAtOnce(Float cosTheta_i, SampledSpectrum etaSpectrum, SampledSpectrum kSpectrum)
        {
            cosTheta_i = Clamp(cosTheta_i, 0, 1);
            SpectrumComplex eta = new SpectrumComplex(etaSpectrum.values, kSpectrum.values);
            SpectrumComplex cosine = new SpectrumComplex(SpectrumVectors.Create(cosTheta_i));
            // Compute complex $\cos\,\theta_\roman{t}$ for Fresnel equations using Snell's law
            Float sin2Theta_i = 1 - Sqr(cosTheta_i);
            SpectrumComplex sin2Theta_t = new SpectrumComplex(SpectrumVectors.Create(sin2Theta_i)) / (eta * eta);
            SpectrumComplex cosTheta_t = SpectrumComplex.Sqrt(new SpectrumComplex(SpectrumVector.One) - sin2Theta_t);

            SpectrumComplex r_parl = (eta * cosine - cosTheta_t) / (eta * cosine + cosTheta_t);
            SpectrumComplex r_perp = (cosine - eta * cosTheta_t) / (cosine + eta * cosTheta_t);
            return new SampledSpectrum((r_parl.Norm() + r_perp.Norm()) / SpectrumVectors.Create((Float)2));
        }

        // FrComplexPolarised() of fresnel_polarised.cs for the index eta + i k of every wavelength
        public static void FrComplexPolarised(Float cosTheta_i, SampledSpectrum etaSpectrum, SampledSpectrum kSpectrum, out SampledSpectrum Fs,
                                              out SampledSpectrum Fp, out SampledSpectrum Ds, out SampledSpectrum Dp)
        {
            cosTheta_i = Clamp(cosTheta_i, 0, 1);
            // The polarised equations assume a negative extinction coefficient (eta = n - ik)
            SpectrumVector k = kSpectrum.values;
            SpectrumComplex eta = new SpectrumComplex(etaSpectrum.values, SpectrumVectors.ConditionalSelect(SpectrumVectors.GreaterThan(k, SpectrumVector.Zero), -k, k));
            SpectrumComplex cosine = new SpectrumComplex(SpectrumVectors.Create(cosTheta_i));

            Float sin2Theta_i = 1 - Sqr(cosTheta_i);
            SpectrumComplex sin2Theta_t = new SpectrumComplex(SpectrumVectors.Create(sin2Theta_i)) / (eta * eta);
            SpectrumComplex cosTheta_t = SpectrumComplex.Sqrt(new SpectrumComplex(SpectrumVector.One) - sin2Theta_t);
            // The root that has a nonpositive imaginary part
            cosTheta_t.im = SpectrumVectors.ConditionalSelect(SpectrumVectors.GreaterThan(cosTheta_t.im, SpectrumVector.Zero), -cosTheta_t.im, cosTheta_t.im);

            SpectrumComplex r_parl = (eta * cosine - cosTheta_t) / (eta * cosine + cosTheta_t);
            SpectrumComplex r_perp = (cosine - eta * cosTheta_t) / (cosine + eta * cosTheta_t);
            Fs = new SampledSpectrum(r_perp.Norm());
            Fp = new SampledSpectrum(r_parl.Norm());
            Ds = new SampledSpectrum(SpectrumVectors.Create(std.atan2(r_perp.im.GetElement(0), r_perp.re.GetElement(0)), std.atan2(r_perp.im.GetElement(1), r_perp.re.GetElement(1)),
                                                            std.atan2(r_perp.im.GetElement(2), r_perp.re.GetElement(2)), std.atan2(r_perp.im.GetElement(3), r_perp.re.GetElement(3))));
            Dp = new SampledSpectrum(SpectrumVectors.Create(std.atan2(r_parl.im.GetElement(0), r_parl.re.GetElement(0)), std.atan2(r_parl.im.GetElement(1), r_parl.re.GetElement(1)),
                                                            std.atan2(r_parl.im.GetElement(2), r_parl.re.GetElement(2)), std.atan2(r_parl.im.GetElement(3), r_parl.re.GetElement(3))));
        }

        // FrThinFilmPolarised() of fresnel_polarised.cs for the four wavelengths of `wavelength` (nanometres) at once. The
        // indices of the layers are those of each wavelength (layerEta0 for the first, and so on; the same array four times
        // for layers that do not disperse), the substrate has the index etaSubstrate - i |kSubstrate| at each.
        public static void FrThinFilmPolarised(Float cosTheta_i, Float etaAmbient, Float[] layerEta0, Float[] layerEta1, Float[] layerEta2, Float[] layerEta3,
                                               Float[] layerThickness, bool reversed, SampledSpectrum wavelength, SampledSpectrum etaSubstrate,
                                               SampledSpectrum kSubstrate, out SampledSpectrum Fs, out SampledSpectrum Fp, out SampledSpectrum Ds,
                                               out SampledSpectrum Dp, out SampledSpectrum DsT, out SampledSpectrum DpT)
        {
            int count = layerEta0.Length;
            double cosI = Math.Clamp((double)cosTheta_i, 0.0, 1.0);
            double snell = etaAmbient * Math.Sqrt(Math.Max(0.0, 1.0 - cosI * cosI));
            WideComplex* index = stackalloc WideComplex[count + 2];
            WideComplex* cosines = stackalloc WideComplex[count + 2];
            index[0] = new WideComplex(Vector256.Create((double)etaAmbient));
            for (int i = 0; i < count; ++i)
            {
                int layer = reversed ? count - 1 - i : i;
                index[i + 1] = new WideComplex(Vector256.Create((double)layerEta0[layer], layerEta1[layer], layerEta2[layer], layerEta3[layer]));
            }
            index[count + 1] = new WideComplex(Vector256.Create((double)etaSubstrate[0], etaSubstrate[1], etaSubstrate[2], etaSubstrate[3]),
                                               -Vector256.Abs(Vector256.Create((double)kSubstrate[0], kSubstrate[1], kSubstrate[2], kSubstrate[3])));
            cosines[0] = new WideComplex(Vector256.Create(cosI));
            WideComplex one = new WideComplex(Vector256<double>.One);
            WideComplex sine = new WideComplex(Vector256.Create(snell));
            for (int i = 1; i < count + 2; ++i)
            {
                WideComplex ratio = sine / index[i];
                // The root with a nonpositive imaginary part
                cosines[i] = WideComplex.SqrtBelow(one - ratio * ratio);
            }

            WideComplex reflectionS = WideInterface(index, cosines, count, true);
            WideComplex reflectionP = WideInterface(index, cosines, count, false);
            WideComplex transmissionS = WideInterfaceTransmission(index, cosines, count, true);
            WideComplex transmissionP = WideInterfaceTransmission(index, cosines, count, false);
            Vector256<double> turn = Vector256.Create(2.0 * Math.PI) /
                                     Vector256.Create((double)wavelength[0], wavelength[1], wavelength[2], wavelength[3]);
            for (int i = count; i >= 1; --i)
            {
                WideComplex delta = index[i] * cosines[i] * (turn * Vector256.Create((double)layerThickness[reversed ? count - i : i - 1]));
                WideComplex across = WideComplex.ExpMinusI(delta);
                WideComplex phase = across * across;
                WideComplex above = WideInterface(index, cosines, i - 1, true);
                WideComplex denominator = one + above * reflectionS * phase;
                transmissionS = WideInterfaceTransmission(index, cosines, i - 1, true) * transmissionS * across / denominator;
                reflectionS = (above + reflectionS * phase) / denominator;
                above = WideInterface(index, cosines, i - 1, false);
                denominator = one + above * reflectionP * phase;
                transmissionP = WideInterfaceTransmission(index, cosines, i - 1, false) * transmissionP * across / denominator;
                reflectionP = (above + reflectionP * phase) / denominator;
            }

            Fs = new SampledSpectrum(0);
            Fp = new SampledSpectrum(0);
            Ds = new SampledSpectrum(0);
            Dp = new SampledSpectrum(0);
            DsT = new SampledSpectrum(0);
            DpT = new SampledSpectrum(0);
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                double sRe = reflectionS.re.GetElement(i), sIm = reflectionS.im.GetElement(i);
                double pRe = reflectionP.re.GetElement(i), pIm = reflectionP.im.GetElement(i);
                Fs[i] = (Float)(sRe * sRe + sIm * sIm);
                Fp[i] = (Float)(pRe * pRe + pIm * pIm);
                Ds[i] = (Float)Math.Atan2(sIm, sRe);
                Dp[i] = (Float)Math.Atan2(pIm, pRe);
                DsT[i] = (Float)Math.Atan2(transmissionS.im.GetElement(i), transmissionS.re.GetElement(i));
                DpT[i] = (Float)Math.Atan2(transmissionP.im.GetElement(i), transmissionP.re.GetElement(i));
            }
        }

        // The amplitude reflection coefficient of the boundary between the media `upper` and `upper + 1`
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static WideComplex WideInterface(WideComplex* index, WideComplex* cosines, int upper, bool perpendicular)
        {
            WideComplex a = index[upper], b = index[upper + 1], ca = cosines[upper], cb = cosines[upper + 1];
            if (perpendicular)
            {
                return (a * ca - b * cb) / (a * ca + b * cb);
            }
            return (b * ca - a * cb) / (b * ca + a * cb);
        }

        // The amplitude transmission coefficient of the same boundary
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static WideComplex WideInterfaceTransmission(WideComplex* index, WideComplex* cosines, int upper, bool perpendicular)
        {
            WideComplex a = index[upper], b = index[upper + 1], ca = cosines[upper], cb = cosines[upper + 1];
            Vector256<double> two = Vector256.Create(2.0);
            if (perpendicular)
            {
                return a * ca * two / (a * ca + b * cb);
            }
            return a * ca * two / (b * ca + a * cb);
        }
    }
}
