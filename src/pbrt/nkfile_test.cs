// (not in pbrt-v4) nkfile_test.cs
//
// The files of measured n and k (nkfile.cs): a metal of pbrt written as a Sopra file in each of its four units and as tables
// (luxpop, with names of columns, without a unit) is read back as the metal; the separators, the comments and the counts that
// files in the wild have; what is wrong with a file is said with its line.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using testing;

namespace pbrt
{
    public static unsafe class IndexFileTests
    {
        static string F(double v)
        {
            return v.ToString("R", CultureInfo.InvariantCulture);
        }

        // How far the file is from the metal over the visible range
        static double Compare(IndexFile file, Spectrum eta, Spectrum k)
        {
            PiecewiseLinearSpectrum n = new PiecewiseLinearSpectrum(file.wavelengths, file.n), kk = new PiecewiseLinearSpectrum(file.wavelengths, file.k);
            double worst = 0;
            for (Float lambda = Lambda_min; lambda <= Lambda_max; lambda += 1)
            {
                worst = Math.Max(worst, Math.Abs(n.Evaluate(lambda) - eta.Evaluate(lambda)));
                worst = Math.Max(worst, Math.Abs(kk.Evaluate(lambda) - k.Evaluate(lambda)));
            }
            return worst;
        }

        [TEST("IndexFiles", "RoundTrip")]
        public static void RoundTrip()
        {
            Spectrum eta = GetNamedSpectrum("metal-Au-eta"), k = GetNamedSpectrum("metal-Au-k");
            // Sopra, 940 intervals evenly spaced in each unit over 360 to 830 nm (and a little more)
            double[] first = { 1239.841984 / 840, 0.35, 1e7 / 840, 350 }, last = { 1239.841984 / 350, 0.84, 1e7 / 350, 840 };
            for (int unit = 1; unit <= 4; ++unit)
            {
                int intervals = 2400;
                StringBuilder text = new StringBuilder();
                text.Append(unit + " " + F(first[unit - 1]) + " " + F(last[unit - 1]) + " " + intervals + "\n");
                for (int i = 0; i <= intervals; ++i)
                {
                    double x = first[unit - 1] + (last[unit - 1] - first[unit - 1]) * i / intervals;
                    Float lambda = (Float)IndexFiles.ToNanometres(x, (IndexFileUnit)unit);
                    text.Append(F(eta.Evaluate(lambda)) + " " + F(k.Evaluate(lambda)) + "\n");
                }
                if (unit == 1)
                {
                    // (kept for a render through the scene parser)
                    System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "magi_gold_sopra.nk"), text.ToString());
                }
                IndexFile file = IndexFiles.Parse(text.ToString());
                EXPECT_EQ("sopra", file.layout);
                EXPECT_EQ(unit, (int)file.unit);
                EXPECT_EQ(0, file.warnings.Count);
                double off = Compare(file, eta, k);
                Console.WriteLine("MEASURE nk file: Sopra in unit {0}, read back {1:E2} from the metal", unit, off);
                EXPECT_LT(off, 4e-3);
                EXPECT_LT(file.wavelengths[0], file.wavelengths[1]);
            }

            // Tables: luxpop (angstroms after comments), names of columns in eV with a fourth column, no unit at all
            StringBuilder luxpop = new StringBuilder("; Optical constants of gold\n; Lambda (A)   n   k\n;-------\n");
            StringBuilder handbook = new StringBuilder("eV\tn\tk\tR(th=0) ; Gold, electropolished\n");
            StringBuilder bare = new StringBuilder();
            for (int i = 0; i <= 1000; ++i)
            {
                Float lambda = 350 + 0.49f * i;
                luxpop.Append("   " + F(lambda * 10) + "   " + F(eta.Evaluate(lambda)) + "   " + F(k.Evaluate(lambda)) + "\n");
                bare.Append(F(lambda) + " " + F(eta.Evaluate(lambda)) + " " + F(k.Evaluate(lambda)) + "\n");
                // (the handbook lists the energies from the smallest: the longest wavelength first)
                Float back = 840 - 0.49f * i;
                handbook.Append(F(1239.841984 / back) + "\t" + F(eta.Evaluate(back)) + "\t" + F(k.Evaluate(back)) + "\t0.9\n");
            }
            System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "magi_gold_luxpop.nk"), luxpop.ToString());
            IndexFile table = IndexFiles.Parse(luxpop.ToString());
            EXPECT_EQ("table", table.layout);
            EXPECT_EQ((int)IndexFileUnit.Angstroms, (int)table.unit);
            EXPECT_EQ(0, table.warnings.Count);
            EXPECT_LT(Compare(table, eta, k), 4e-3);
            table = IndexFiles.Parse(handbook.ToString());
            EXPECT_EQ((int)IndexFileUnit.ElectronVolts, (int)table.unit);
            EXPECT_LT(Compare(table, eta, k), 4e-3);
            table = IndexFiles.Parse(bare.ToString());
            EXPECT_EQ((int)IndexFileUnit.Nanometres, (int)table.unit);
            EXPECT_EQ(1, table.warnings.Count);
            EXPECT_LT(Compare(table, eta, k), 4e-3);
        }

        [TEST("IndexFiles", "FilesInTheWild")]
        public static void FilesInTheWild()
        {
            // The example of the Sopra documentation, cut to four intervals: eV, from 1.45 to 3.5 (855 to 354 nm)
            IndexFile file = IndexFiles.Parse("1 1.45 3.5 4\n1.064 14.4\n.987875 13.155\n.844 12.2\n.672625 11.445\n.514 10.8\n");
            EXPECT_EQ(0, file.warnings.Count);
            // (the wavelengths increase: the last energy first)
            EXPECT_LT(Math.Abs(file.wavelengths[0] - 1239.841984 / 3.5), 1e-3);
            EXPECT_LT(Math.Abs(file.n[0] - 0.514), 1e-6);
            EXPECT_LT(Math.Abs(file.wavelengths[4] - 1239.841984 / 1.45), 1e-3);
            EXPECT_LT(Math.Abs(file.k[4] - 14.4), 1e-5);
            EXPECT_LT(Math.Abs(file.wavelengths[2] - 1239.841984 / 2.475), 1e-3);
            EXPECT_LT(Math.Abs(file.n[2] - 0.844), 1e-6);

            // The same with semicolons, commas, several pairs on a line, exponents with D, Windows line ends, empty lines at
            // the end and a comment
            IndexFile same = IndexFiles.Parse("1 1.45 3.5 4   # gold\r\n1.064; 14.4;\r\n.987875,13.155 .844 1.22D1\r\n.672625 11.445\r\n5.14e-1 10.8\r\n\r\n\r\n");
            EXPECT_EQ(5, same.wavelengths.Length);
            for (int i = 0; i < 5; ++i)
            {
                EXPECT_EQ(file.wavelengths[i], same.wavelengths[i]);
                EXPECT_EQ(file.n[i], same.n[i]);
                EXPECT_EQ(file.k[i], same.k[i]);
            }
            // A count of points where the count of intervals should be: read, and said
            IndexFile points = IndexFiles.Parse("4 400 800 3\n1 2\n1.5 2.5\n2 3\n");
            EXPECT_EQ(1 + 1, points.warnings.Count);  // (and the ends of the visible range)
            EXPECT_LT(Math.Abs(points.wavelengths[2] - 600), 1e-3);
            EXPECT_LT(Math.Abs(points.n[2] - 1.5), 1e-6);

            // Outside of its table a spectrum would be zero: the ends are held
            PiecewiseLinearSpectrum n = new PiecewiseLinearSpectrum(points.wavelengths, points.n);
            EXPECT_LT(Math.Abs(n.Evaluate(365) - 1), 1e-6);
            EXPECT_LT(Math.Abs(n.Evaluate(825) - 2), 1e-6);

            // A table: rows in any order, a repeated wavelength keeps its first row, k with a sign
            IndexFile table = IndexFiles.Parse("; nm  n  k\n700 1.3 -3\n400 1.1 1\n550 1.2 2\n550 9 9\n");
            EXPECT_EQ(5, table.wavelengths.Length);
            EXPECT_LT(Math.Abs(table.n[2] - 1.2), 1e-6);
            EXPECT_LT(Math.Abs(table.k[3] - 3), 1e-6);
            EXPECT_EQ(3, table.warnings.Count);
            // (a table of four columns whose first row could be the first line of a Sopra file is a table)
            IndexFile four = IndexFiles.Parse("; eV n k R\n2 1.5 3.5 4\n3 1.6 3.6 5\n");
            EXPECT_EQ("table", four.layout);

            // What is wrong is said
            string[] wrong = { "", "; only a comment\n", "1 1.5 3.5 4\n1 2\n3 4\n", "; nm n k\n500 1.2\n", "; nm n k\n500 1.2 2\nabc\n", "4 400 800 2\n1 2\n1 x\n1 2\n",
                               "; nm n k\n-5 1 2\n", "4 0 800 1\n1 2\n1 2\n" };
            foreach (string text in wrong)
            {
                bool thrown = false;
                try
                {
                    IndexFiles.Parse(text);
                }
                catch (FormatException e)
                {
                    thrown = true;
                    Console.WriteLine("MEASURE nk file error: " + e.Message);
                }
                EXPECT_TRUE(thrown, text);
            }

            EXPECT_TRUE(IndexFiles.IsIndexFile("gold.nk"));
            EXPECT_TRUE(IndexFiles.IsIndexFile("dir/Gold.NK:k"));
            EXPECT_TRUE(IndexFiles.IsIndexFile("zinc.ior:n"));
            EXPECT_FALSE(IndexFiles.IsIndexFile("gold.spd"));
            EXPECT_FALSE(IndexFiles.IsIndexFile("metal-Au-eta"));
        }
    }
}
