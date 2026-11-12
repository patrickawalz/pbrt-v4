// (not in pbrt-v4) abbespectrum_test.cs

using System;
using testing;

namespace pbrt
{
    public static class AbbeDispersionSpectrumTests
    {
        // The definition holds exactly: the index at the D line, and (n_D - 1) / (n_F - n_C) is the Abbe number
        [TEST("AbbeDispersionSpectrum", "ReproducesTheIndexAndTheAbbeNumber")]
        public static void AbbeDispersionSpectrum_ReproducesTheIndexAndTheAbbeNumber()
        {
            Float[][] glasses = new Float[][] { new Float[] { 1.5168f, 64.17f }, new Float[] { 1.62004f, 36.37f },
                                                new Float[] { 1.7847f, 25.68f }, new Float[] { 2.417f, 55.3f } };
            foreach (Float[] glass in glasses)
            {
                AbbeDispersionSpectrum n = new AbbeDispersionSpectrum(glass[0], glass[1]);
                Float nD = n.Evaluate(AbbeDispersionSpectrum.LambdaD);
                Float nF = n.Evaluate(AbbeDispersionSpectrum.LambdaF);
                Float nC = n.Evaluate(AbbeDispersionSpectrum.LambdaC);
                EXPECT_LT(Math.Abs(nD - glass[0]), 2e-6);
                EXPECT_LT(Math.Abs((nD - 1) / (nF - nC) - glass[1]) / glass[1], 2e-3);
                EXPECT_GE(n.MaxValue(), n.Evaluate(400));
            }
        }

        // Schott N-BK7 (n_d 1.5168, V_d 64.17): the catalogue indices at four other lines, to the fourth decimal
        [TEST("AbbeDispersionSpectrum", "MatchesTheCatalogueIndicesOfBK7")]
        public static void AbbeDispersionSpectrum_MatchesTheCatalogueIndicesOfBK7()
        {
            AbbeDispersionSpectrum n = new AbbeDispersionSpectrum(1.5168f, 64.17f);
            Float[][] lines = new Float[][] { new Float[] { 404.66f, 1.53024f }, new Float[] { 435.83f, 1.52668f },
                                              new Float[] { 486.13f, 1.52238f }, new Float[] { 656.27f, 1.51432f },
                                              new Float[] { 706.52f, 1.51289f } };
            double worst = 0;
            foreach (Float[] line in lines)
            {
                worst = Math.Max(worst, Math.Abs(n.Evaluate(line[0]) - line[1]));
            }
            Console.Error.WriteLine("MEASURE abbe: BK7, worst difference to the catalogue index over 405-707 nm " + worst +
                                    ", Cauchy A " + n.CauchyA() + " B " + n.CauchyB());
            EXPECT_LT(worst, 1e-3);
        }
    }
}
