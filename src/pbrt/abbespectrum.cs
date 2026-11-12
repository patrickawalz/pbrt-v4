// (not in pbrt-v4) abbespectrum.cs
//
// The index of refraction of a dielectric given the way an optical glass catalogue (and Maxwell Render) gives it: the index
// n_D at the sodium D line and the Abbe number
//
//     V_D = (n_D - 1) / (n_F - n_C)
//
// with n_F and n_C the indices at the hydrogen F and C lines. Two numbers fix a dispersion formula with two coefficients, and
// the one that glasses follow in the visible is Cauchy's with two terms,
//
//     n(lambda) = A + B / lambda^2
//
// From the definition of V_D,  B = (n_D - 1) / (V_D (1 / lambda_F^2 - 1 / lambda_C^2)),  and from the index at the D line,
// A = n_D - B / lambda_D^2. The index at the D line and the difference n_F - n_C are then exact, whatever the glass.

using System;

namespace pbrt
{
    // AbbeDispersionSpectrum Definition
    public sealed class AbbeDispersionSpectrum : Spectrum
    {
        // The Fraunhofer lines of the definition, in nm
        public const Float LambdaD = 589.29f;
        public const Float LambdaF = 486.13f;
        public const Float LambdaC = 656.27f;

        // AbbeDispersionSpectrum Public Methods
        public AbbeDispersionSpectrum(Float nD, Float abbe)
        {
            if (!(nD >= 1))
            {
                pbrt.ErrorExit(pbrt.StringPrintf("The index of refraction %f of a dielectric with an Abbe number is below 1.", nD));
            }
            if (!(abbe > 0))
            {
                pbrt.ErrorExit(pbrt.StringPrintf("The Abbe number %f is not positive.", abbe));
            }
            this.nD = nD;
            this.abbe = abbe;
            double inverseF = 1.0 / ((double)LambdaF * LambdaF);
            double inverseC = 1.0 / ((double)LambdaC * LambdaC);
            double inverseD = 1.0 / ((double)LambdaD * LambdaD);
            double b = (nD - 1.0) / (abbe * (inverseF - inverseC));
            B = (Float)b;
            A = (Float)(nD - b * inverseD);
        }

        // The Cauchy coefficients: n(lambda) = A + B / lambda^2, lambda in nm
        public Float CauchyA()
        {
            return A;
        }

        public Float CauchyB()
        {
            return B;
        }

        public override Float Evaluate(Float lambda)
        {
            return A + B / (lambda * lambda);
        }

        // The index falls with the wavelength: the largest value is at the short end of the range that is sampled
        public override Float MaxValue()
        {
            return Evaluate(pbrt.Lambda_min);
        }

        public override SampledSpectrum Sample(SampledWavelengths lambda)
        {
            SampledSpectrum s = new SampledSpectrum(0.0f);
            for (int i = 0; i < pbrt.NSpectrumSamples; ++i)
            {
                s[i] = Evaluate(lambda[i]);
            }
            return s;
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ AbbeDispersionSpectrum nD: %f abbe: %f A: %f B: %f ]", nD, abbe, A, B);
        }

        // AbbeDispersionSpectrum Private Members
        Float nD;
        Float abbe;
        Float A;
        Float B;
    }
}
