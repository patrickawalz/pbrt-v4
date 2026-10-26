// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// cmd/rgb2spec_opt.cpp
//
// Unlike the C++ tool (which writes a C++ translation unit), this port writes the table as
// compiled-in C# source (rgbspectrum_*.cs), in the format used by Magi's tables/ projects.

using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;

public static class Rgb2SpecOpt
{
    /**
     * This file contains:
     *
     * 1. CIE 1931 curves at sampled at 5nm intervals
     *
     * 2. CIE D65 and D50 spectra sampled at 5nm intervals.
     *    Both are normalized to have unit luminance.
     *
     * 3. XYZ <-> sRGB conversion matrices
     *    XYZ <-> ProPhoto RGB conversion matrices
     *
     * 4. A convenience function "cie_interp" to access the discretized
     *    data at arbitrary wavelengths (with linear interpolation)
     */
    const double CIE_LAMBDA_MIN = 360.0;
    const double CIE_LAMBDA_MAX = 830.0;
    const int CIE_SAMPLES = 95;

    static readonly double[] cie_x = {
        0.000129900000, 0.000232100000, 0.000414900000, 0.000741600000, 0.001368000000,
        0.002236000000, 0.004243000000, 0.007650000000, 0.014310000000, 0.023190000000,
        0.043510000000, 0.077630000000, 0.134380000000, 0.214770000000, 0.283900000000,
        0.328500000000, 0.348280000000, 0.348060000000, 0.336200000000, 0.318700000000,
        0.290800000000, 0.251100000000, 0.195360000000, 0.142100000000, 0.095640000000,
        0.057950010000, 0.032010000000, 0.014700000000, 0.004900000000, 0.002400000000,
        0.009300000000, 0.029100000000, 0.063270000000, 0.109600000000, 0.165500000000,
        0.225749900000, 0.290400000000, 0.359700000000, 0.433449900000, 0.512050100000,
        0.594500000000, 0.678400000000, 0.762100000000, 0.842500000000, 0.916300000000,
        0.978600000000, 1.026300000000, 1.056700000000, 1.062200000000, 1.045600000000,
        1.002600000000, 0.938400000000, 0.854449900000, 0.751400000000, 0.642400000000,
        0.541900000000, 0.447900000000, 0.360800000000, 0.283500000000, 0.218700000000,
        0.164900000000, 0.121200000000, 0.087400000000, 0.063600000000, 0.046770000000,
        0.032900000000, 0.022700000000, 0.015840000000, 0.011359160000, 0.008110916000,
        0.005790346000, 0.004109457000, 0.002899327000, 0.002049190000, 0.001439971000,
        0.000999949300, 0.000690078600, 0.000476021300, 0.000332301100, 0.000234826100,
        0.000166150500, 0.000117413000, 0.000083075270, 0.000058706520, 0.000041509940,
        0.000029353260, 0.000020673830, 0.000014559770, 0.000010253980, 0.000007221456,
        0.000005085868, 0.000003581652, 0.000002522525, 0.000001776509, 0.000001251141};

    static readonly double[] cie_y = {
        0.000003917000, 0.000006965000, 0.000012390000, 0.000022020000, 0.000039000000,
        0.000064000000, 0.000120000000, 0.000217000000, 0.000396000000, 0.000640000000,
        0.001210000000, 0.002180000000, 0.004000000000, 0.007300000000, 0.011600000000,
        0.016840000000, 0.023000000000, 0.029800000000, 0.038000000000, 0.048000000000,
        0.060000000000, 0.073900000000, 0.090980000000, 0.112600000000, 0.139020000000,
        0.169300000000, 0.208020000000, 0.258600000000, 0.323000000000, 0.407300000000,
        0.503000000000, 0.608200000000, 0.710000000000, 0.793200000000, 0.862000000000,
        0.914850100000, 0.954000000000, 0.980300000000, 0.994950100000, 1.000000000000,
        0.995000000000, 0.978600000000, 0.952000000000, 0.915400000000, 0.870000000000,
        0.816300000000, 0.757000000000, 0.694900000000, 0.631000000000, 0.566800000000,
        0.503000000000, 0.441200000000, 0.381000000000, 0.321000000000, 0.265000000000,
        0.217000000000, 0.175000000000, 0.138200000000, 0.107000000000, 0.081600000000,
        0.061000000000, 0.044580000000, 0.032000000000, 0.023200000000, 0.017000000000,
        0.011920000000, 0.008210000000, 0.005723000000, 0.004102000000, 0.002929000000,
        0.002091000000, 0.001484000000, 0.001047000000, 0.000740000000, 0.000520000000,
        0.000361100000, 0.000249200000, 0.000171900000, 0.000120000000, 0.000084800000,
        0.000060000000, 0.000042400000, 0.000030000000, 0.000021200000, 0.000014990000,
        0.000010600000, 0.000007465700, 0.000005257800, 0.000003702900, 0.000002607800,
        0.000001836600, 0.000001293400, 0.000000910930, 0.000000641530, 0.000000451810};

    static readonly double[] cie_z = {
        0.000606100000, 0.001086000000, 0.001946000000, 0.003486000000, 0.006450001000,
        0.010549990000, 0.020050010000, 0.036210000000, 0.067850010000, 0.110200000000,
        0.207400000000, 0.371300000000, 0.645600000000, 1.039050100000, 1.385600000000,
        1.622960000000, 1.747060000000, 1.782600000000, 1.772110000000, 1.744100000000,
        1.669200000000, 1.528100000000, 1.287640000000, 1.041900000000, 0.812950100000,
        0.616200000000, 0.465180000000, 0.353300000000, 0.272000000000, 0.212300000000,
        0.158200000000, 0.111700000000, 0.078249990000, 0.057250010000, 0.042160000000,
        0.029840000000, 0.020300000000, 0.013400000000, 0.008749999000, 0.005749999000,
        0.003900000000, 0.002749999000, 0.002100000000, 0.001800000000, 0.001650001000,
        0.001400000000, 0.001100000000, 0.001000000000, 0.000800000000, 0.000600000000,
        0.000340000000, 0.000240000000, 0.000190000000, 0.000100000000, 0.000049999990,
        0.000030000000, 0.000020000000, 0.000010000000, 0.000000000000, 0.000000000000,
        0.000000000000, 0.000000000000, 0.000000000000, 0.000000000000, 0.000000000000,
        0.000000000000, 0.000000000000, 0.000000000000, 0.000000000000, 0.000000000000,
        0.000000000000, 0.000000000000, 0.000000000000, 0.000000000000, 0.000000000000,
        0.000000000000, 0.000000000000, 0.000000000000, 0.000000000000, 0.000000000000,
        0.000000000000, 0.000000000000, 0.000000000000, 0.000000000000, 0.000000000000,
        0.000000000000, 0.000000000000, 0.000000000000, 0.000000000000, 0.000000000000,
        0.000000000000, 0.000000000000, 0.000000000000, 0.000000000000, 0.000000000000};

    static double N65(double x) { return (x / 10566.864005283874576); }

    static readonly double[] cie_d65 = {
        N65(46.6383), N65(49.3637), N65(52.0891), N65(51.0323), N65(49.9755), N65(52.3118), N65(54.6482),
        N65(68.7015), N65(82.7549), N65(87.1204), N65(91.486),  N65(92.4589), N65(93.4318), N65(90.057),
        N65(86.6823), N65(95.7736), N65(104.865), N65(110.936), N65(117.008), N65(117.41),  N65(117.812),
        N65(116.336), N65(114.861), N65(115.392), N65(115.923), N65(112.367), N65(108.811), N65(109.082),
        N65(109.354), N65(108.578), N65(107.802), N65(106.296), N65(104.79),  N65(106.239), N65(107.689),
        N65(106.047), N65(104.405), N65(104.225), N65(104.046), N65(102.023), N65(100.0),   N65(98.1671),
        N65(96.3342), N65(96.0611), N65(95.788),  N65(92.2368), N65(88.6856), N65(89.3459), N65(90.0062),
        N65(89.8026), N65(89.5991), N65(88.6489), N65(87.6987), N65(85.4936), N65(83.2886), N65(83.4939),
        N65(83.6992), N65(81.863),  N65(80.0268), N65(80.1207), N65(80.2146), N65(81.2462), N65(82.2778),
        N65(80.281),  N65(78.2842), N65(74.0027), N65(69.7213), N65(70.6652), N65(71.6091), N65(72.979),
        N65(74.349),  N65(67.9765), N65(61.604),  N65(65.7448), N65(69.8856), N65(72.4863), N65(75.087),
        N65(69.3398), N65(63.5927), N65(55.0054), N65(46.4182), N65(56.6118), N65(66.8054), N65(65.0941),
        N65(63.3828), N65(63.8434), N65(64.304),  N65(61.8779), N65(59.4519), N65(55.7054), N65(51.959),
        N65(54.6998), N65(57.4406), N65(58.8765), N65(60.3125)};


    static double NE(double x) { return (x / 106.8); }
    static readonly double[] cie_e = {
        NE(1.0), NE(1.0), NE(1.0), NE(1.0), NE(1.0), NE(1.0), NE(1.0), NE(1.0), NE(1.0), NE(1.0),
        NE(1.0), NE(1.0), NE(1.0), NE(1.0), NE(1.0), NE(1.0), NE(1.0), NE(1.0), NE(1.0), NE(1.0),
        NE(1.0), NE(1.0), NE(1.0), NE(1.0), NE(1.0), NE(1.0), NE(1.0), NE(1.0), NE(1.0), NE(1.0),
        NE(1.0), NE(1.0), NE(1.0), NE(1.0), NE(1.0), NE(1.0), NE(1.0), NE(1.0), NE(1.0), NE(1.0),
        NE(1.0), NE(1.0), NE(1.0), NE(1.0), NE(1.0), NE(1.0), NE(1.0), NE(1.0), NE(1.0), NE(1.0),
        NE(1.0), NE(1.0), NE(1.0), NE(1.0), NE(1.0), NE(1.0), NE(1.0), NE(1.0), NE(1.0), NE(1.0),
        NE(1.0), NE(1.0), NE(1.0), NE(1.0), NE(1.0), NE(1.0), NE(1.0), NE(1.0), NE(1.0), NE(1.0),
        NE(1.0), NE(1.0), NE(1.0), NE(1.0), NE(1.0), NE(1.0), NE(1.0), NE(1.0), NE(1.0), NE(1.0),
        NE(1.0), NE(1.0), NE(1.0), NE(1.0), NE(1.0), NE(1.0), NE(1.0), NE(1.0), NE(1.0), NE(1.0),
        NE(1.0), NE(1.0), NE(1.0), NE(1.0), NE(1.0)};

    static double N50(double x) { return (x / 10503.2); }

    static readonly double[] cie_d50 = {
        N50(23.942000),  N50(25.451000),  N50(26.961000),  N50(25.724000),  N50(24.488000),
        N50(27.179000),  N50(29.871000),  N50(39.589000),  N50(49.308000),  N50(52.910000),
        N50(56.513000),  N50(58.273000),  N50(60.034000),  N50(58.926000),  N50(57.818000),
        N50(66.321000),  N50(74.825000),  N50(81.036000),  N50(87.247000),  N50(88.930000),
        N50(90.612000),  N50(90.990000),  N50(91.368000),  N50(93.238000),  N50(95.109000),
        N50(93.536000),  N50(91.963000),  N50(93.843000),  N50(95.724000),  N50(96.169000),
        N50(96.613000),  N50(96.871000),  N50(97.129000),  N50(99.614000),  N50(102.099000),
        N50(101.427000), N50(100.755000), N50(101.536000), N50(102.317000), N50(101.159000),
        N50(100.000000), N50(98.868000),  N50(97.735000),  N50(98.327000),  N50(98.918000),
        N50(96.208000),  N50(93.499000),  N50(95.593000),  N50(97.688000),  N50(98.478000),
        N50(99.269000),  N50(99.155000),  N50(99.042000),  N50(97.382000),  N50(95.722000),
        N50(97.290000),  N50(98.857000),  N50(97.262000),  N50(95.667000),  N50(96.929000),
        N50(98.190000),  N50(100.597000), N50(103.003000), N50(101.068000), N50(99.133000),
        N50(93.257000),  N50(87.381000),  N50(89.492000),  N50(91.604000),  N50(92.246000),
        N50(92.889000),  N50(84.872000),  N50(76.854000),  N50(81.683000),  N50(86.511000),
        N50(89.546000),  N50(92.580000),  N50(85.405000),  N50(78.230000),  N50(67.961000),
        N50(57.692000),  N50(70.307000),  N50(82.923000),  N50(80.599000),  N50(78.274000),
        N50(0),          N50(0),          N50(0),          N50(0),          N50(0),
        N50(0),          N50(0),          N50(0),          N50(0)};


    static double N60(double x) { return (x / 10536.3); }

    static readonly double[] cie_d60 = {
        N60(38.683115),  N60(41.014457),  N60(42.717548),  N60(42.264182),  N60(41.454941),
        N60(41.763698),  N60(46.605319),  N60(59.226938),  N60(72.278594),  N60(78.231500),
        N60(80.440600),  N60(82.739580),  N60(82.915027),  N60(79.009168),  N60(77.676264),
        N60(85.163609),  N60(95.681274),  N60(103.267764), N60(107.954821), N60(109.777964),
        N60(109.559187), N60(108.418402), N60(107.758141), N60(109.071548), N60(109.671404),
        N60(106.734741), N60(103.707873), N60(103.981942), N60(105.232199), N60(105.235867),
        N60(104.427667), N60(103.052881), N60(102.522934), N60(104.371416), N60(106.052671),
        N60(104.948900), N60(103.315154), N60(103.416286), N60(103.538599), N60(102.099304),
        N60(100.000000), N60(97.992725),  N60(96.751421),  N60(97.102402),  N60(96.712823),
        N60(93.174457),  N60(89.921479),  N60(90.351933),  N60(91.999793),  N60(92.384009),
        N60(92.098710),  N60(91.722859),  N60(90.646003),  N60(88.327552),  N60(86.526483),
        N60(87.034239),  N60(87.579186),  N60(85.884584),  N60(83.976140),  N60(83.743140),
        N60(84.724074),  N60(86.450818),  N60(87.493491),  N60(86.546330),  N60(83.483070),
        N60(78.268785),  N60(74.172451),  N60(74.275184),  N60(76.620385),  N60(79.423856),
        N60(79.051849),  N60(71.763360),  N60(65.471371),  N60(67.984085),  N60(74.106079),
        N60(78.556612),  N60(79.527120),  N60(75.584935),  N60(67.307163),  N60(55.275106),
        N60(49.273538),  N60(59.008629),  N60(70.892412),  N60(70.950115),  N60(67.163996),
        N60(67.445480),  N60(68.171371),  N60(66.466636),  N60(62.989809),  N60(58.067786),
        N60(54.990892),  N60(56.915942),  N60(60.825601),  N60(62.987850)};


    static readonly double[,] xyz_to_srgb = {{3.240479, -1.537150, -0.498535},
                                      {-0.969256, 1.875991, 0.041556},
                                      {0.055648, -0.204043, 1.057311}};

    static readonly double[,] srgb_to_xyz = {{0.412453, 0.357580, 0.180423},
                                      {0.212671, 0.715160, 0.072169},
                                      {0.019334, 0.119193, 0.950227}};

    static readonly double[,] xyz_to_xyz = {
        {1.0, 0.0, 0.0},
        {0.0, 1.0, 0.0},
        {0.0, 0.0, 1.0},
    };

    static readonly double[,] xyz_to_ergb = {
        {2.689989, -1.276020, -0.413844},
        {-1.022095, 1.978261, 0.043821},
        {0.061203, -0.224411, 1.162859},
    };

    static readonly double[,] ergb_to_xyz = {
        {0.496859, 0.339094, 0.164047},
        {0.256193, 0.678188, 0.065619},
        {0.023290, 0.113031, 0.863978},
    };

    static readonly double[,] xyz_to_prophoto_rgb = {{1.3459433, -0.2556075, -0.0511118},
                                              {-0.5445989, 1.5081673, 0.0205351},
                                              {0.0000000, 0.0000000, 1.2118128}};

    static readonly double[,] prophoto_rgb_to_xyz = {{0.7976749, 0.1351917, 0.0313534},
                                              {0.2880402, 0.7118741, 0.0000857},
                                              {0.0000000, 0.0000000, 0.8252100}};

    static readonly double[,] xyz_to_aces2065_1 = {{1.0498110175, 0.0000000000, -0.0000974845},
                                            {-0.4959030231, 1.3733130458, 0.0982400361},
                                            {0.0000000000, 0.0000000000, 0.9912520182}};

    static readonly double[,] aces2065_1_to_xyz = {{0.9525523959, 0.0000000000, 0.0000936786},
                                            {0.3439664498, 0.7281660966, -0.0721325464},
                                            {0.0000000000, 0.0000000000, 1.0088251844}};

    static readonly double[,] xyz_to_rec2020 = {{1.7166511880, -0.3556707838, -0.2533662814},
                                         {-0.6666843518, 1.6164812366, 0.0157685458},
                                         {0.0176398574, -0.0427706133, 0.9421031212}};

    static readonly double[,] rec2020_to_xyz = {{0.6369580483, 0.1446169036, 0.1688809752},
                                         {0.2627002120, 0.6779980715, 0.0593017165},
                                         {0.0000000000, 0.0280726930, 1.0609850577}};

    static readonly double[,] xyz_to_dcip3 = {{2.4931748, -0.93126315, -0.40265882},
                                       {-0.82950425, 1.7626965, 0.023625137},
                                       {0.035853732, -0.07618918, 0.9570952}};
    static readonly double[,] dcip3_to_xyz = {{0.48663378, 0.26566276, 0.19817366},
                                       {0.22900413, 0.69172573, 0.079269454},
                                       {0.0, 0.04511256, 1.0437145}};

    static double cie_interp(double[] data, double x)
    {
        x -= CIE_LAMBDA_MIN;
        x *= (CIE_SAMPLES - 1) / (CIE_LAMBDA_MAX - CIE_LAMBDA_MIN);
        int offset = (int)x;
        if (offset < 0)
            offset = 0;
        if (offset > CIE_SAMPLES - 2)
            offset = CIE_SAMPLES - 2;
        double weight = x - offset;
        return (1.0 - weight) * data[offset] + weight * data[offset + 1];
    }

    // LU decomposition & triangular solving code lifted from Wikipedia

    /* INPUT: A - array of pointers to rows of a square matrix having dimension N
     *        Tol - small tolerance number to detect failure when the matrix is near
     * degenerate OUTPUT: Matrix A is changed, it contains both matrices L-E and U
     * as A=(L-E)+U such that P*A=L*U. The permutation matrix is not stored as a
     * matrix, but in an integer vector P of size N+1 containing column indexes
     * where the permutation matrix has "1". The last element P[N]=S+N, where S is
     * the number of row exchanges needed for determinant computation, det(P)=(-1)^S
     */
    static int LUPDecompose(double[][] A, int N, double Tol, int[] P)
    {
        int i, j, k, imax;
        double maxA, absA;
        double[] ptr;

        for (i = 0; i <= N; i++)
            P[i] = i;  // Unit permutation matrix, P[N] initialized with N

        for (i = 0; i < N; i++)
        {
            maxA = 0.0;
            imax = i;

            for (k = i; k < N; k++)
                if ((absA = Math.Abs(A[k][i])) > maxA)
                {
                    maxA = absA;
                    imax = k;
                }

            if (maxA < Tol)
                return 0;  // failure, matrix is degenerate

            if (imax != i)
            {
                // pivoting P
                j = P[i];
                P[i] = P[imax];
                P[imax] = j;

                // pivoting rows of A
                ptr = A[i];
                A[i] = A[imax];
                A[imax] = ptr;

                // counting pivots starting from N (for determinant)
                P[N]++;
            }

            for (j = i + 1; j < N; j++)
            {
                A[j][i] /= A[i][i];

                for (k = i + 1; k < N; k++)
                    A[j][k] -= A[j][i] * A[i][k];
            }
        }

        return 1;  // decomposition done
    }

    /* INPUT: A,P filled in LUPDecompose; b - rhs vector; N - dimension
     * OUTPUT: x - solution vector of A*x=b
     */
    static void LUPSolve(double[][] A, int[] P, double[] b, int N, double[] x)
    {
        for (int i = 0; i < N; i++)
        {
            x[i] = b[P[i]];

            for (int k = 0; k < i; k++)
                x[i] -= A[i][k] * x[k];
        }

        for (int i = N - 1; i >= 0; i--)
        {
            for (int k = i + 1; k < N; k++)
                x[i] -= A[i][k] * x[k];

            x[i] = x[i] / A[i][i];
        }
    }

    /// Discretization of quadrature scheme
    const int CIE_FINE_SAMPLES = ((CIE_SAMPLES - 1) * 3 + 1);
    const double RGB2SPEC_EPSILON = 1e-4;

    /// Precomputed tables for fast spectral -> RGB conversion
    static double[] lambda_tbl = new double[CIE_FINE_SAMPLES];
    static double[,] rgb_tbl = new double[3, CIE_FINE_SAMPLES];
    static double[,] rgb_to_xyz = new double[3, 3];
    static double[,] xyz_to_rgb = new double[3, 3];
    static double[] xyz_whitepoint = new double[3];

    /// Currently supported gamuts
    enum Gamut
    {
        SRGB,
        ProPhotoRGB,
        ACES2065_1,
        REC2020,
        ERGB,
        XYZ,
        DCI_P3,
        NO_GAMUT,
    }

    static double sigmoid(double x)
    {
        return 0.5 * x / Math.Sqrt(1.0 + x * x) + 0.5;
    }

    static double smoothstep(double x)
    {
        return x * x * (3.0 - 2.0 * x);
    }

    static double sqr(double x)
    {
        return x * x;
    }

    static double cie_lab_f(double t)
    {
        double delta = 6.0 / 29.0;
        if (t > delta * delta * delta)
            return Math.Cbrt(t);
        else
            return t / (delta * delta * 3.0) + (4.0 / 29.0);
    }

    static void cie_lab(double[] p)
    {
        double X = 0.0, Y = 0.0, Z = 0.0, Xw = xyz_whitepoint[0], Yw = xyz_whitepoint[1],
               Zw = xyz_whitepoint[2];

        for (int j = 0; j < 3; ++j)
        {
            X += p[j] * rgb_to_xyz[0, j];
            Y += p[j] * rgb_to_xyz[1, j];
            Z += p[j] * rgb_to_xyz[2, j];
        }

        p[0] = 116.0 * cie_lab_f(Y / Yw) - 16.0;
        p[1] = 500.0 * (cie_lab_f(X / Xw) - cie_lab_f(Y / Yw));
        p[2] = 200.0 * (cie_lab_f(Y / Yw) - cie_lab_f(Z / Zw));
    }

    static void copy33(double[,] dst, double[,] src)
    {
        Array.Copy(src, dst, 9);
    }

    /**
     * This function precomputes tables used to convert arbitrary spectra
     * to RGB (either sRGB or ProPhoto RGB)
     *
     * A composite quadrature rule integrates the CIE curves, reflectance, and
     * illuminant spectrum over each 5nm segment in the 360..830nm range using
     * Simpson's 3/8 rule (4th-order accurate), which evaluates the integrand at
     * four positions per segment. While the CIE curves and illuminant spectrum are
     * linear over the segment, the reflectance could have arbitrary behavior,
     * hence the extra precations.
     */
    static void init_tables(Gamut gamut)
    {
        Array.Clear(rgb_tbl, 0, rgb_tbl.Length);
        Array.Clear(xyz_whitepoint, 0, xyz_whitepoint.Length);

        double h = (CIE_LAMBDA_MAX - CIE_LAMBDA_MIN) / (CIE_FINE_SAMPLES - 1);

        double[] illuminant = null;

        switch (gamut)
        {
        case Gamut.SRGB:
            illuminant = cie_d65;
            copy33(xyz_to_rgb, xyz_to_srgb);
            copy33(rgb_to_xyz, srgb_to_xyz);
            break;

        case Gamut.ERGB:
            illuminant = cie_e;
            copy33(xyz_to_rgb, xyz_to_ergb);
            copy33(rgb_to_xyz, ergb_to_xyz);
            break;

        case Gamut.XYZ:
            illuminant = cie_e;
            copy33(xyz_to_rgb, xyz_to_xyz);
            copy33(rgb_to_xyz, xyz_to_xyz);
            break;

        case Gamut.ProPhotoRGB:
            illuminant = cie_d50;
            copy33(xyz_to_rgb, xyz_to_prophoto_rgb);
            copy33(rgb_to_xyz, prophoto_rgb_to_xyz);
            break;

        case Gamut.ACES2065_1:
            illuminant = cie_d60;
            copy33(xyz_to_rgb, xyz_to_aces2065_1);
            copy33(rgb_to_xyz, aces2065_1_to_xyz);
            break;

        case Gamut.REC2020:
            illuminant = cie_d65;
            copy33(xyz_to_rgb, xyz_to_rec2020);
            copy33(rgb_to_xyz, rec2020_to_xyz);
            break;

        case Gamut.DCI_P3:
            illuminant = cie_d65;
            copy33(xyz_to_rgb, xyz_to_dcip3);
            copy33(rgb_to_xyz, dcip3_to_xyz);
            break;

        default:
            throw new InvalidOperationException("init_gamut(): invalid/unsupported gamut.");
        }

        for (int i = 0; i < CIE_FINE_SAMPLES; ++i)
        {
            double lambda = CIE_LAMBDA_MIN + i * h;

            double[] xyz = { cie_interp(cie_x, lambda), cie_interp(cie_y, lambda),
                             cie_interp(cie_z, lambda) };
            double I = cie_interp(illuminant, lambda);

            double weight = 3.0 / 8.0 * h;
            if (i == 0 || i == CIE_FINE_SAMPLES - 1)
                ;
            else if ((i - 1) % 3 == 2)
                weight *= 2.0;
            else
                weight *= 3.0;

            lambda_tbl[i] = lambda;
            for (int k = 0; k < 3; ++k)
                for (int j = 0; j < 3; ++j)
                    rgb_tbl[k, i] += xyz_to_rgb[k, j] * xyz[j] * I * weight;

            for (int ii = 0; ii < 3; ++ii)
                xyz_whitepoint[ii] += xyz[ii] * I * weight;
        }
    }

    static void eval_residual(double[] coeffs, double[] rgb, double[] residual)
    {
        double[] outv = { 0.0, 0.0, 0.0 };

        for (int i = 0; i < CIE_FINE_SAMPLES; ++i)
        {
            /* Scale lambda to 0..1 range */
            double lambda =
                (lambda_tbl[i] - CIE_LAMBDA_MIN) / (CIE_LAMBDA_MAX - CIE_LAMBDA_MIN);

            /* Polynomial */
            double x = 0.0;
            for (int ii = 0; ii < 3; ++ii)
                x = x * lambda + coeffs[ii];

            /* Sigmoid */
            double s = sigmoid(x);

            /* Integrate against precomputed curves */
            for (int j = 0; j < 3; ++j)
                outv[j] += rgb_tbl[j, i] * s;
        }
        cie_lab(outv);
        Array.Copy(rgb, residual, 3);
        cie_lab(residual);

        for (int j = 0; j < 3; ++j)
            residual[j] -= outv[j];
    }

    static void eval_jacobian(double[] coeffs, double[] rgb, double[][] jac)
    {
        double[] r0 = new double[3], r1 = new double[3], tmp = new double[3];

        for (int i = 0; i < 3; ++i)
        {
            Array.Copy(coeffs, tmp, 3);
            tmp[i] -= RGB2SPEC_EPSILON;
            eval_residual(tmp, rgb, r0);

            Array.Copy(coeffs, tmp, 3);
            tmp[i] += RGB2SPEC_EPSILON;
            eval_residual(tmp, rgb, r1);

            for (int j = 0; j < 3; ++j)
                jac[j][i] = (r1[j] - r0[j]) * 1.0 / (2 * RGB2SPEC_EPSILON);
        }
    }

    static void gauss_newton(double[] rgb, double[] coeffs, int it = 15)
    {
        double r = 0;
        for (int i = 0; i < it; ++i)
        {
            double[] J0 = new double[3], J1 = new double[3], J2 = new double[3];
            double[][] J = { J0, J1, J2 };

            double[] residual = new double[3];

            eval_residual(coeffs, rgb, residual);
            eval_jacobian(coeffs, rgb, J);

            int[] P = new int[4];
            int rv = LUPDecompose(J, 3, 1e-15, P);
            if (rv != 1)
            {
                Console.Out.WriteLine("RGB " + G(rgb[0]) + " " + G(rgb[1]) + " " + G(rgb[2]));
                Console.Out.WriteLine("-> " + G(coeffs[0]) + " " + G(coeffs[1]) + " " + G(coeffs[2]));
                throw new InvalidOperationException("LU decomposition failed!");
            }

            double[] x = new double[3];
            LUPSolve(J, P, residual, 3, x);

            r = 0.0;
            for (int j = 0; j < 3; ++j)
            {
                coeffs[j] -= x[j];
                r += residual[j] * residual[j];
            }
            double max = Math.Max(Math.Max(coeffs[0], coeffs[1]), coeffs[2]);

            if (max > 200)
            {
                for (int j = 0; j < 3; ++j)
                    coeffs[j] *= 200 / max;
            }

            if (r < 1e-6)
                break;
        }
    }

    static Gamut parse_gamut(string str)
    {
        if (string.Equals(str, "sRGB", StringComparison.OrdinalIgnoreCase))
            return Gamut.SRGB;
        if (string.Equals(str, "eRGB", StringComparison.OrdinalIgnoreCase))
            return Gamut.ERGB;
        if (string.Equals(str, "XYZ", StringComparison.OrdinalIgnoreCase))
            return Gamut.XYZ;
        if (string.Equals(str, "ProPhotoRGB", StringComparison.OrdinalIgnoreCase))
            return Gamut.ProPhotoRGB;
        if (string.Equals(str, "ACES2065_1", StringComparison.OrdinalIgnoreCase))
            return Gamut.ACES2065_1;
        if (string.Equals(str, "REC2020", StringComparison.OrdinalIgnoreCase))
            return Gamut.REC2020;
        if (string.Equals(str, "DCI_P3", StringComparison.OrdinalIgnoreCase))
            return Gamut.DCI_P3;
        return Gamut.NO_GAMUT;
    }

    // C's "%.9g" of a double (here: of a float promoted to double) or "%.6g" via precision.
    static string G(double v, int prec = 6)
    {
        if (double.IsNaN(v)) return "-nan";
        if (double.IsInfinity(v)) return v > 0 ? "inf" : "-inf";
        if (v == 0) return (1 / v < 0) ? "-0" : "0";
        string e = v.ToString("E" + (prec - 1), CultureInfo.InvariantCulture);  // d.ddddE+XXX
        int ePos = e.IndexOf('E');
        int exp = int.Parse(e.Substring(ePos + 1), CultureInfo.InvariantCulture);
        string r;
        if (exp < -4 || exp >= prec)
        {
            string mant = e.Substring(0, ePos);
            if (mant.Contains('.')) mant = mant.TrimEnd('0').TrimEnd('.');
            r = mant + "e" + (exp < 0 ? "-" : "+") + Math.Abs(exp).ToString("00", CultureInfo.InvariantCulture);
        }
        else
        {
            r = v.ToString("F" + Math.Max(0, prec - 1 - exp), CultureInfo.InvariantCulture);
            if (r.Contains('.')) r = r.TrimEnd('0').TrimEnd('.');
        }
        return r;
    }

    static string G9f(float v) { return G((double)v, 9) + "f"; }

    static int atoi(string s)
    {
        int i = 0, n = s.Length;
        while (i < n && char.IsWhiteSpace(s[i])) ++i;
        bool neg = false;
        if (i < n && (s[i] == '+' || s[i] == '-')) { neg = s[i] == '-'; ++i; }
        long v = 0;
        while (i < n && s[i] >= '0' && s[i] <= '9') { v = v * 10 + (s[i] - '0'); if (v > int.MaxValue) v = int.MaxValue; ++i; }
        return (int)(neg ? -v : v);
    }

    public static int Main(string[] argv)
    {
        int argc = argv.Length + 1;
        if (argc < 3)
        {
            Console.Out.Write("Syntax: rgb2spec_opt <resolution> <output> [<gamut>]\n" +
                              "where <gamut> is one of " +
                              "sRGB,eRGB,XYZ,ProPhotoRGB,ACES2065_1,REC2020,DCI_P3\n");
            return 255;
        }
        Gamut gamut = Gamut.SRGB;
        if (argc > 3)
            gamut = parse_gamut(argv[2]);
        if (gamut == Gamut.NO_GAMUT)
        {
            Console.Error.Write("Could not parse gamut `" + argv[2] + "'!\n");
            return 255;
        }
        init_tables(gamut);

        int res = atoi(argv[0]);
        if (res == 0)
        {
            Console.Out.Write("Invalid resolution!\n");
            return 255;
        }

        string gamutName = argc > 3 ? argv[2] : "(null)";
        Console.Out.Write("Optimizing " + gamutName + " spectra...\n");
        Console.Out.Flush();

        float[] scale = new float[res];
        for (int k = 0; k < res; ++k)
            scale[k] = (float)smoothstep(smoothstep(k / (double)(res - 1)));

        long bufsize = 3L * 3 * res * res * res;
        float[] outp = new float[bufsize];

        for (int l = 0; l < 3; ++l)
        {
            int ll = l;
            Parallel.For(0, res, (int j) =>
            {
                double y = j / (double)(res - 1);
                for (int i = 0; i < res; ++i)
                {
                    double x = i / (double)(res - 1);
                    double[] coeffs = new double[3], rgb = new double[3];

                    int start = res / 5;

                    for (int k = start; k < res; ++k)
                    {
                        double b = (double)scale[k];

                        rgb[ll] = b;
                        rgb[(ll + 1) % 3] = x * b;
                        rgb[(ll + 2) % 3] = y * b;

                        gauss_newton(rgb, coeffs);

                        double c0 = 360.0, c1 = 1.0 / (830.0 - 360.0);
                        double A = coeffs[0], B = coeffs[1], C = coeffs[2];

                        int idx = ((ll * res + k) * res + j) * res + i;

                        outp[3 * idx + 0] = (float)(A * (sqr(c1)));
                        outp[3 * idx + 1] = (float)(B * c1 - 2 * A * c0 * (sqr(c1)));
                        outp[3 * idx + 2] = (float)(C - B * c0 * c1 + A * (sqr(c0 * c1)));
                    }

                    Array.Clear(coeffs, 0, 3);
                    for (int k = start; k >= 0; --k)
                    {
                        double b = (double)scale[k];

                        rgb[ll] = b;
                        rgb[(ll + 1) % 3] = x * b;
                        rgb[(ll + 2) % 3] = y * b;

                        gauss_newton(rgb, coeffs);

                        double c0 = 360.0, c1 = 1.0 / (830.0 - 360.0);
                        double A = coeffs[0], B = coeffs[1], C = coeffs[2];

                        int idx = ((ll * res + k) * res + j) * res + i;

                        outp[3 * idx + 0] = (float)(A * (sqr(c1)));
                        outp[3 * idx + 1] = (float)(B * c1 - 2 * A * c0 * (sqr(c1)));
                        outp[3 * idx + 2] = (float)(C - B * c0 * c1 + A * (sqr(c0 * c1)));
                    }
                }
            });
        }

        string className = Path.GetFileNameWithoutExtension(argv[1]);
        StringBuilder sb = new StringBuilder();
        sb.Append("// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.\n");
        sb.Append("// The pbrt source code is licensed under the Apache License, Version 2.0.\n");
        sb.Append("// SPDX: Apache-2.0\n");
        sb.Append("//\n");
        sb.Append("// Generated by rgb2spec_opt " + argv[0] + " " + Path.GetFileName(argv[1]) + " " + gamutName +
                  " (pbrt-v4 cmd/rgb2spec_opt.cpp), as the pbrt CMake\n");
        sb.Append("// build does. float[3][" + res + "][" + res + "][" + res + "][3] is stored flattened in C order.\n");
        sb.Append("\nnamespace pbrt\n{\n\n");
        sb.Append("    public static class " + className + "\n    {\n");
        sb.Append("        public static readonly int " + gamutName + "ToSpectrumTable_Res = " + res + ";\n\n");
        sb.Append("        public static readonly float[] " + gamutName + "ToSpectrumTable_Scale = {\n");
        for (int i = 0; i < res; i += 8)
        {
            sb.Append("            ");
            for (int j = i; j < Math.Min(res, i + 8); ++j)
            {
                if (j > i) sb.Append(' ');
                sb.Append(G9f(scale[j])).Append(',');
            }
            sb.Append('\n');
        }
        sb.Append("        };\n\n");
        sb.Append("        public static readonly float[] " + gamutName + "ToSpectrumTable_Data = {\n");
        for (long i = 0; i < bufsize; i += 12)
        {
            sb.Append("            ");
            for (long j = i; j < Math.Min(bufsize, i + 12); ++j)
            {
                if (j > i) sb.Append(' ');
                sb.Append(G9f(outp[j])).Append(',');
            }
            sb.Append('\n');
        }
        sb.Append("        };\n\n    }\n\n}  // namespace pbrt\n");

        try
        {
            File.WriteAllText(argv[1], sb.ToString(), new UTF8Encoding(false));
        }
        catch (Exception)
        {
            throw new InvalidOperationException("Could not create file!");
        }

        return 0;
    }
}
