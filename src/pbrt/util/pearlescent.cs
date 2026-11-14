// (not in pbrt-v4) The optics of a pearlescent platelet (Guillen, Marco, Gutierrez, Jakob and Jarabo 2020, "A General
// Framework for Pearlescent Materials", section 4): a substrate (mica, silica) with a coating (titanium dioxide) on both
// sides, inside the container medium. Nothing absorbs, so the transmittance is one minus the reflectance.
//
// The reflectance is the average over the Gaussian distribution of the thickness of the substrate, in closed form (no
// tables). With z = exp(-2 i delta) the phase of a round trip in the substrate, the amplitude of a platelet is
//   r = a + b z / (1 - c z),   a = r_top,   b = t_top t_top' r_a,   c = r_a^2
// (r_top: the coating on the substrate seen from the container; r_a: the coating seen from inside the substrate), and with
// W(k) = <z^k> = exp(-2 i k d) exp(-2 k^2 s^2) (d, s: mean and standard deviation of delta)
//   <r1 r2*> = a1 a2* + a1 b2* sum_n c2*^n W(n+1)* + a2* b1 sum_m c1^m W(m+1) + b1 b2* sum_{m,n} c1^m c2*^n W(m-n).
// With both amplitudes those of one polarisation this is its reflectance; with the two polarisations it is the term that
// carries the phase between them, the third and fourth rows of the Mueller matrix.
//
// The amplitudes and the sign of the phase are those of FrThinFilmPolarised() (fresnel_polarised.cs).

using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace pbrt
{
    // PearlescentStack Definition
    public struct PearlescentStack
    {
        // The index of the container
        public double containerEta;
        // The coating: its index at 587.6 nm, its Abbe number and its thickness in nanometres
        public double coatingEta, coatingAbbe, coatingThickness;
        // The substrate: the same, and the standard deviation of its thickness in nanometres
        public double substrateEta, substrateAbbe, substrateThickness, substrateDeviation;
    }

    public static unsafe class Pearlescent
    {
        const int MaxTerms = 80;

        // The index at a wavelength (nanometres), from the index at the d line and the Abbe number
        public static double Cauchy(double etaD, double abbe, double wavelength)
        {
            const double lineF = 486.1, lineC = 656.3, lineD = 587.6;
            double B = (etaD - 1) / (abbe * (1 / (lineF * lineF) - 1 / (lineC * lineC)));
            double A = etaD - B / (lineD * lineD);
            return A + B / (wavelength * wavelength);
        }

        // The cosine inside a layer of index eta, for the cosine cosContainer in the container. The root with a nonpositive
        // imaginary part: the wave decays.
        static Complex CosineIn(double containerEta, double cosContainer, double eta)
        {
            double sin2 = (1 - cosContainer * cosContainer) * (containerEta / eta) * (containerEta / eta);
            Complex cosine = Complex.Sqrt(new Complex(1 - sin2, 0));
            if (cosine.Imaginary > 0)
            {
                cosine = -cosine;
            }
            return cosine;
        }

        // The Fresnel amplitude reflection coefficient from a to b
        static Complex Fresnel(bool perpendicular, double etaA, Complex cosA, double etaB, Complex cosB)
        {
            if (perpendicular)
            {
                return (etaA * cosA - etaB * cosB) / (etaA * cosA + etaB * cosB);
            }
            return (etaB * cosA - etaA * cosB) / (etaB * cosA + etaA * cosB);
        }

        // a, b and c of one polarisation
        static void Coefficients(bool perpendicular, double containerEta, double cosContainer, double coatingEta, Complex cosCoating,
                                 double substrateEta, Complex cosSubstrate, Complex coatingPhase, out Complex a, out Complex b, out Complex c)
        {
            Complex outer = Fresnel(perpendicular, containerEta, cosContainer, coatingEta, cosCoating);
            Complex inner = Fresnel(perpendicular, coatingEta, cosCoating, substrateEta, cosSubstrate);
            Complex denominator = 1.0 + outer * inner * coatingPhase;
            a = (outer + inner * coatingPhase) / denominator;
            Complex below = -(inner + outer * coatingPhase) / denominator;
            b = (1.0 - outer * outer) * (1.0 - inner * inner) * coatingPhase / (denominator * denominator) * below;
            c = below * below;
        }

        // The number of terms of the series for a ratio c: term k is of the size |c|^k gauss^(k^2), gauss = exp(-2 s^2) the
        // factor by which the spread of the thickness damps W(1)
        static int Terms(Complex c, double gauss)
        {
            double magnitude = Math.Min(Math.Sqrt(c.Real * c.Real + c.Imaginary * c.Imaginary), 0.999999);
            double size = 1, step = magnitude * gauss, gaussSquared = gauss * gauss;
            int terms = 0;
            while (terms < MaxTerms && size > 1e-9)
            {
                size *= step;
                step *= gaussSquared;
                ++terms;
            }
            return Math.Max(terms, 2);
        }

        // <r1 r2*> from the coefficients of the two amplitudes and W(0..terms). The pairs (m, n) of the double sum with
        // m - n = k add up to c1^k / (1 - c1 c2*) (and c2*^|k| / (1 - c1 c2*) for k < 0): only the sum over k is cut, where
        // W(k) has made its terms negligible.
        static Complex Correlation(Complex a1, Complex b1, Complex c1, Complex a2, Complex b2, Complex c2, Complex* W, int terms)
        {
            Complex conjugateC2 = Complex.Conjugate(c2);
            Complex first = 0.0, second = 0.0, both = 1.0;
            Complex power1 = 1.0, power2 = 1.0;
            for (int k = 0; k < terms; ++k)
            {
                first += power1 * W[k + 1];
                second += power2 * Complex.Conjugate(W[k + 1]);
                if (k > 0)
                {
                    both += power1 * W[k] + power2 * Complex.Conjugate(W[k]);
                }
                power1 *= c1;
                power2 *= conjugateC2;
            }
            both /= 1.0 - c1 * conjugateC2;
            return a1 * Complex.Conjugate(a2) + a1 * Complex.Conjugate(b2) * second + Complex.Conjugate(a2) * b1 * first +
                   b1 * Complex.Conjugate(b2) * both;
        }

        // The reflectance of a platelet for the two polarisations, averaged over the thickness of the substrate, for light
        // that meets it at the cosine cosContainer in the container; and <r_s r_p*>, the real part C and the imaginary part S
        // of what Attenuation.Fresnel() builds from the two phases (sqrt(Fs Fp) cos(Ds - Dp) and sin(Ds - Dp); smaller than
        // sqrt(Fs Fp) where the platelets of different thickness disagree about the phase: they depolarise).
        [SkipLocalsInit]
        public static void Reflectance(double cosContainer, double wavelength, PearlescentStack stack, out double Fs, out double Fp, out double C,
                                       out double S)
        {
            cosContainer = Math.Min(Math.Max(cosContainer, 1e-4), 1.0);
            double coatingEta = Cauchy(stack.coatingEta, stack.coatingAbbe, wavelength);
            double substrateEta = Cauchy(stack.substrateEta, stack.substrateAbbe, wavelength);
            Complex cosCoating = CosineIn(stack.containerEta, cosContainer, coatingEta);
            Complex cosSubstrate = CosineIn(stack.containerEta, cosContainer, substrateEta);
            Complex coatingPhase = Complex.Exp(new Complex(0, -2) * (2 * Math.PI * coatingEta * cosCoating * stack.coatingThickness / wavelength));
            Coefficients(true, stack.containerEta, cosContainer, coatingEta, cosCoating, substrateEta, cosSubstrate, coatingPhase, out Complex aS,
                         out Complex bS, out Complex cS);
            Coefficients(false, stack.containerEta, cosContainer, coatingEta, cosCoating, substrateEta, cosSubstrate, coatingPhase, out Complex aP,
                         out Complex bP, out Complex cP);

            if (Math.Abs(cosSubstrate.Imaginary) > 1e-9 || !(stack.substrateDeviation > 0))
            {
                // One thickness: the amplitude itself. Also under total reflection at the substrate (a container of higher
                // index), where the wave tunnels through a platelet of the mean thickness.
                Complex across = Complex.Exp(new Complex(0, -2) * (2 * Math.PI * substrateEta * cosSubstrate * stack.substrateThickness / wavelength));
                Complex rS = aS + bS * across / (1.0 - cS * across), rP = aP + bP * across / (1.0 - cP * across);
                Complex cross = rS * Complex.Conjugate(rP);
                Fs = Math.Min(rS.Real * rS.Real + rS.Imaginary * rS.Imaginary, 1.0);
                Fp = Math.Min(rP.Real * rP.Real + rP.Imaginary * rP.Imaginary, 1.0);
                C = cross.Real;
                S = cross.Imaginary;
                return;
            }

            double mean = 2 * Math.PI * substrateEta * cosSubstrate.Real * stack.substrateThickness / wavelength;
            double deviation = 2 * Math.PI * substrateEta * cosSubstrate.Real * stack.substrateDeviation / wavelength;
            // W(k) = u^k g^(k^2), and g^((k+1)^2) = g^(k^2) g^(2k+1)
            Complex* W = stackalloc Complex[MaxTerms + 1];
            (double sinMean, double cosMean) = Math.SinCos(-2.0 * mean);
            Complex unit = new Complex(cosMean, sinMean), unitPower = 1.0;
            double gauss = Math.Exp(-2.0 * deviation * deviation), gaussSquared = gauss * gauss, gaussPower = 1, gaussStep = gauss;
            int termsS = Terms(cS, gauss), termsP = Terms(cP, gauss), terms = Math.Max(termsS, termsP);
            for (int k = 0; k <= terms; ++k)
            {
                W[k] = unitPower * gaussPower;
                unitPower *= unit;
                gaussPower *= gaussStep;
                gaussStep *= gaussSquared;
            }
            Fs = Math.Min(Math.Max(Correlation(aS, bS, cS, aS, bS, cS, W, termsS).Real, 0.0), 1.0);
            Fp = Math.Min(Math.Max(Correlation(aP, bP, cP, aP, bP, cP, W, termsP).Real, 0.0), 1.0);
            Complex between = Correlation(aS, bS, cS, aP, bP, cP, W, terms);
            // The mean of a product is never above the product of the root mean squares: what rounding may break
            double limit = Math.Sqrt(Fs * Fp), size = Complex.Abs(between);
            if (size > limit && size > 0)
            {
                between *= limit / size;
            }
            C = between.Real;
            S = between.Imaginary;
        }

        // The reflectance for unpolarised light
        [SkipLocalsInit]
        public static double Reflectance(double cosContainer, double wavelength, PearlescentStack stack)
        {
            cosContainer = Math.Min(Math.Max(cosContainer, 1e-4), 1.0);
            double coatingEta = Cauchy(stack.coatingEta, stack.coatingAbbe, wavelength);
            double substrateEta = Cauchy(stack.substrateEta, stack.substrateAbbe, wavelength);
            Complex cosCoating = CosineIn(stack.containerEta, cosContainer, coatingEta);
            Complex cosSubstrate = CosineIn(stack.containerEta, cosContainer, substrateEta);
            Complex coatingPhase = Complex.Exp(new Complex(0, -2) * (2 * Math.PI * coatingEta * cosCoating * stack.coatingThickness / wavelength));
            Coefficients(true, stack.containerEta, cosContainer, coatingEta, cosCoating, substrateEta, cosSubstrate, coatingPhase, out Complex aS,
                         out Complex bS, out Complex cS);
            Coefficients(false, stack.containerEta, cosContainer, coatingEta, cosCoating, substrateEta, cosSubstrate, coatingPhase, out Complex aP,
                         out Complex bP, out Complex cP);

            if (Math.Abs(cosSubstrate.Imaginary) > 1e-9 || !(stack.substrateDeviation > 0))
            {
                Complex across = Complex.Exp(new Complex(0, -2) * (2 * Math.PI * substrateEta * cosSubstrate * stack.substrateThickness / wavelength));
                Complex rS = aS + bS * across / (1.0 - cS * across), rP = aP + bP * across / (1.0 - cP * across);
                return Math.Min(0.5 * (rS.Real * rS.Real + rS.Imaginary * rS.Imaginary + rP.Real * rP.Real + rP.Imaginary * rP.Imaginary), 1.0);
            }

            double mean = 2 * Math.PI * substrateEta * cosSubstrate.Real * stack.substrateThickness / wavelength;
            double deviation = 2 * Math.PI * substrateEta * cosSubstrate.Real * stack.substrateDeviation / wavelength;
            Complex* W = stackalloc Complex[MaxTerms + 1];
            (double sinMean, double cosMean) = Math.SinCos(-2.0 * mean);
            Complex unit = new Complex(cosMean, sinMean), unitPower = 1.0;
            double gauss = Math.Exp(-2.0 * deviation * deviation), gaussSquared = gauss * gauss, gaussPower = 1, gaussStep = gauss;
            int termsS = Terms(cS, gauss), termsP = Terms(cP, gauss), terms = Math.Max(termsS, termsP);
            for (int k = 0; k <= terms; ++k)
            {
                W[k] = unitPower * gaussPower;
                unitPower *= unit;
                gaussPower *= gaussStep;
                gaussStep *= gaussSquared;
            }
            double sum = Correlation(aS, bS, cS, aS, bS, cS, W, termsS).Real + Correlation(aP, bP, cP, aP, bP, cP, W, termsP).Real;
            return Math.Min(Math.Max(0.5 * sum, 0.0), 1.0);
        }
    }
}
