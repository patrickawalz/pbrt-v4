//
//   Copyright 2013 Pixar
//
//   Licensed under the terms set forth in the LICENSE.txt file available at
//   https://opensubdiv.org/license.
//

using System;
using System.Collections.Generic;

namespace OpenSubdiv.Far.Internal
{
    //
    //  Patch basis evaluation (Far::internal in the C++).  The C++ function
    //  templates over REAL (float or double) are expressed here as overloads
    //  for float and double with identical text.  Null arrays denote the
    //  optional (unrequested) weight arrays.
    //
    public static class PatchBasis
    {
        private static readonly int[] boundaryGregory = new int[12] { 0, 1, 7, 5, 2, 6, 16, 12, 15, 17, 11, 10 };
        private static readonly int[] boundaryBezSCol = new int[12] { 0, 1, 2, 3, 0, 3,  0,  3,  0,  1,  2,  3 };
        private static readonly int[] boundaryBezTRow = new int[12] { 0, 0, 0, 0, 1, 1,  2,  2,  3,  3,  3,  3 };
        private static readonly int[] interiorGregory = new int[8] { 3, 4,  8, 9,  13, 14,  18, 19 };
        private static readonly int[] interiorBezSCol = new int[8] { 1, 1,  2, 2,   2,  2,   1,  1 };
        private static readonly int[] interiorBezTRow = new int[8] { 1, 1,  1, 1,   2,  2,   2,  2 };

        //
        //  float versions
        //


        //
        //  Basis support for quadrilateral patches:
        //
        //  Quadrilateral patches are parameterized in terms of (s,t) as follows:
        //
        //      (1,0) *---------* (1,1)
        //            | 3     2 |
        //          t |         |
        //            |         |
        //            | 0     1 |
        //      (0,0) *---------* (1,0)
        //                 s
        //

        //
        //  Simple bilinear quad:
        //
        public static int EvalBasisLinear(float s, float t,
        float[] wP, float[] wDs = null, float[] wDt = null,
        float[] wDss = null, float[] wDst = null, float[] wDtt = null)
        {

            float sC = 1.0f - s;
            float tC = 1.0f - t;

            if (wP != null)
            {
                wP[0] = sC * tC;
                wP[1] =  s * tC;
                wP[2] =  s * t;
                wP[3] = sC * t;
            }
            if (wDs != null && wDt != null)
            {
                wDs[0] = -tC;
                wDs[1] =  tC;
                wDs[2] =   t;
                wDs[3] =  -t;

                wDt[0] = -sC;
                wDt[1] =  -s;
                wDt[2] =   s;
                wDt[3] =  sC;

                if (wDss != null && wDst != null && wDtt != null)
                {
                    for(int i = 0; i < 4; ++i)
                    {
                        wDss[i] = 0.0f;
                        wDtt[i] = 0.0f;
                    }

                    wDst[0] =  1.0f;
                    wDst[1] = -1.0f;
                    wDst[2] =  1.0f;
                    wDst[3] = -1.0f;
                }
            }
            return 4;
        }

        //
        //  Bicubic BSpline patch:
        //
        //     12-----13------14-----15
        //      |      |      |      |
        //      |      |      |      |
        //      8------9------10-----11
        //      |      | t    |      |
        //      |      |   s  |      |
        //      4------5------6------7
        //      |      |      |      |
        //      |      |      |      |
        //      O------1------2------3
        //
        //  The basis if a bicubic BSpline patch is a tensor product, which we make
        //  use of here by evaluating, differentiating and combining basis functions
        //  in each of the two parametric directions.
        //
        //  Not all 16 points will be present.  The boundary mask indicates boundary
        //  edges beyond which phantom points are implicitly extrapolated.  Weights
        //  for missing points are set to zero while those contributing to their
        //  implicit extrapolation will be adjusted.
        //
        //
        //  Cubic BSpline curve basis evaluation:
        //
        public static void evalBSplineCurve(float t, float[] wP, float[] wDP, float[] wDP2)
        {

            float one6th = (float)(1.0 / 6.0);

            float t2 = t * t;
            float t3 = t * t2;

            wP[0] = one6th * (1.0f - 3.0f*(t -      t2) -      t3);
            wP[1] = one6th * (4.0f           - 6.0f*t2  + 3.0f*t3);
            wP[2] = one6th * (1.0f + 3.0f*(t +      t2  -      t3));
            wP[3] = one6th * (                                 t3);

            if (wDP != null)
            {
                wDP[0] = -0.5f*t2 +      t - 0.5f;
                wDP[1] =  1.5f*t2 - 2.0f*t;
                wDP[2] = -1.5f*t2 +      t + 0.5f;
                wDP[3] =  0.5f*t2;
            }
            if (wDP2 != null)
            {
                wDP2[0] = -       t + 1.0f;
                wDP2[1] =  3.0f * t - 2.0f;
                wDP2[2] = -3.0f * t + 1.0f;
                wDP2[3] =         t;
            }
        }

        //
        //  Weight adjustments to account for phantom end points:
        //
        public static void adjustBSplineBoundaryWeights(int boundary, float[] w)
        {

            if ((boundary & 1) != 0)
            {
                for (int i = 0; i < 4; ++i)
                {
                    w[i + 8] -= w[i + 0];
                    w[i + 4] += w[i + 0] * 2.0f;
                    w[i + 0]  = 0.0f;
                }
            }
            if ((boundary & 2) != 0)
            {
                for (int i = 0; i < 16; i += 4)
                {
                    w[i + 1] -= w[i + 3];
                    w[i + 2] += w[i + 3] * 2.0f;
                    w[i + 3]  = 0.0f;
                }
            }
            if ((boundary & 4) != 0)
            {
                for (int i = 0; i < 4; ++i)
                {
                    w[i +  4] -= w[i + 12];
                    w[i +  8] += w[i + 12] * 2.0f;
                    w[i + 12]  = 0.0f;
                }
            }
            if ((boundary & 8) != 0)
            {
                for (int i = 0; i < 16; i += 4)
                {
                    w[i + 2] -= w[i + 0];
                    w[i + 1] += w[i + 0] * 2.0f;
                    w[i + 0]  = 0.0f;
                }
            }
        }

        public static void boundBasisBSpline(int boundary,
        float[] wP, float[] wDs, float[] wDt,
        float[] wDss, float[] wDst, float[] wDtt)
        {

            if (wP != null)
            {
                adjustBSplineBoundaryWeights(boundary, wP);
            }
            if (wDs != null && wDt != null)
            {
                adjustBSplineBoundaryWeights(boundary, wDs);
                adjustBSplineBoundaryWeights(boundary, wDt);

                if (wDss != null && wDst != null && wDtt != null)
                {
                    adjustBSplineBoundaryWeights(boundary, wDss);
                    adjustBSplineBoundaryWeights(boundary, wDst);
                    adjustBSplineBoundaryWeights(boundary, wDtt);
                }
            }
        }

        public static int EvalBasisBSpline(float s, float t,
        float[] wP, float[] wDs = null, float[] wDt = null,
        float[] wDss = null, float[] wDst = null, float[] wDtt = null)
        {

            float[] sWeights = new float[4];
            float[] tWeights = new float[4];
            float[] dsWeights = new float[4];
            float[] dtWeights = new float[4];
            float[] dssWeights = new float[4];
            float[] dttWeights = new float[4];

            evalBSplineCurve(s, wP != null ? sWeights : null, wDs != null ? dsWeights : null, wDss != null ? dssWeights : null);
            evalBSplineCurve(t, wP != null ? tWeights : null, wDt != null ? dtWeights : null, wDtt != null ? dttWeights : null);

            if (wP != null)
            {
                for (int i = 0; i < 4; ++i)
                {
                    for (int j = 0; j < 4; ++j)
                    {
                        wP[4*i+j] = sWeights[j] * tWeights[i];
                    }
                }
            }
            if (wDs != null && wDt != null)
            {
                for (int i = 0; i < 4; ++i)
                {
                    for (int j = 0; j < 4; ++j)
                    {
                        wDs[4*i+j] = dsWeights[j] * tWeights[i];
                        wDt[4*i+j] = sWeights[j] * dtWeights[i];
                    }
                }

                if (wDss != null && wDst != null && wDtt != null)
                {
                    for (int i = 0; i < 4; ++i)
                    {
                        for (int j = 0; j < 4; ++j)
                        {
                            wDss[4*i+j] = dssWeights[j] * tWeights[i];
                            wDst[4*i+j] = dsWeights[j] * dtWeights[i];
                            wDtt[4*i+j] = sWeights[j] * dttWeights[i];
                        }
                    }
                }
            }
            return 16;
        }

        //
        //  Bicubic Bezier patch:
        //
        //     12-----13------14-----15
        //      |      |      |      |
        //      |      |      |      |
        //      8------9------10-----11
        //      |      |      |      |
        //      |      |      |      |
        //      4------5------6------7
        //      | t    |      |      |
        //      |   s  |      |      |
        //      O------1------2------3
        //
        //  As was the case with the BSpline patch, a bicubic Bezier patch can also
        //  make use of its tensor product property by evaluating, differentiating
        //  and combining basis functions in each of the two parametric directions.
        //
        //
        //  Cubic Bezier curve basis evaluation:
        //
        public static void evalBezierCurve(float t, float[] wP, float[] wDP, float[] wDP2)
        {

            // The four uniform cubic Bezier basis functions (in terms of t and its
            // complement tC) evaluated at t:
            float t2 = t*t;
            float tC = 1.0f - t;
            float tC2 = tC * tC;

            wP[0] = tC2 * tC;
            wP[1] = tC2 * t * 3.0f;
            wP[2] = t2 * tC * 3.0f;
            wP[3] = t2 * t;

            // Derivatives of the above four basis functions at t:
            if (wDP != null)
            {
                wDP[0] = -3.0f * tC2;
                wDP[1] =  9.0f * t2 - 12.0f * t + 3.0f;
                wDP[2] = -9.0f * t2 +  6.0f * t;
                wDP[3] =  3.0f * t2;
            }

            // Second derivatives of the basis functions at t:
            if (wDP2 != null)
            {
                wDP2[0] =   6.0f * tC;
                wDP2[1] =  18.0f * t - 12.0f;
                wDP2[2] = -18.0f * t +  6.0f;
                wDP2[3] =   6.0f * t;
            }
        }

        public static int EvalBasisBezier(float s, float t,
        float[] wP, float[] wDs = null, float[] wDt = null,
        float[] wDss = null, float[] wDst = null, float[] wDtt = null)
        {

            float[] sWeights = new float[4];
            float[] tWeights = new float[4];
            float[] dsWeights = new float[4];
            float[] dtWeights = new float[4];
            float[] dssWeights = new float[4];
            float[] dttWeights = new float[4];

            evalBezierCurve(s, wP != null ? sWeights : null, wDs != null ? dsWeights : null, wDss != null ? dssWeights : null);
            evalBezierCurve(t, wP != null ? tWeights : null, wDt != null ? dtWeights : null, wDtt != null ? dttWeights : null);

            if (wP != null)
            {
                for (int i = 0; i < 4; ++i)
                {
                    for (int j = 0; j < 4; ++j)
                    {
                        wP[4*i+j] = sWeights[j] * tWeights[i];
                    }
                }
            }
            if (wDs != null && wDt != null)
            {
                for (int i = 0; i < 4; ++i)
                {
                    for (int j = 0; j < 4; ++j)
                    {
                        wDs[4*i+j] = dsWeights[j] * tWeights[i];
                        wDt[4*i+j] = sWeights[j] * dtWeights[i];
                    }
                }

                if (wDss != null && wDst != null && wDtt != null)
                {
                    for (int i = 0; i < 4; ++i)
                    {
                        for (int j = 0; j < 4; ++j)
                        {
                            wDss[4*i+j] = dssWeights[j] * tWeights[i];
                            wDst[4*i+j] = dsWeights[j] * dtWeights[i];
                            wDtt[4*i+j] = sWeights[j] * dttWeights[i];
                        }
                    }
                }
            }
            return 16;
        }

        //
        //  Cubic Gregory patch:
        //
        //      P3         e3-      e2+         P2
        //         15------17-------11--------10
        //         |        |        |        |
        //         |        |        |        |
        //         |        | f3-    | f2+    |
        //         |       19       13        |
        //     e3+ 16-----18           14-----12 e2-
        //         |     f3+          f2-     |
        //         |                          |
        //         |                          |
        //         |      f0-         f1+     |
        //     e0- 2------4            8------6 e1+
        //         |        3        9        |
        //         |        | f0+    | f1-    |
        //         | t      |        |        |
        //         |   s    |        |        |
        //         O--------1--------7--------5
        //      P0         e0+      e1-         P1
        //
        //  The 20-point cubic Gregory patch is an extension of the 16-point bicubic
        //  Bezier patch with the 4 interior points of the Bezier patch replaced with
        //  pairs of points (face points -- fi+ and fi-) that are rationally combined.
        //
        //  The point ordering of the Gregory patch deviates considerably from the
        //  BSpline and Bezier patches by grouping the 5 points at each corner and
        //  ordering the groups by corner index.
        //
        public static int EvalBasisGregory(float s, float t,
        float[] point, float[] wDs = null, float[] wDt = null,
        float[] wDss = null, float[] wDst = null, float[] wDtt = null)
        {

            //  Indices of boundary and interior points and their corresponding Bezier points
            //  (this can be reduced with more direct indexing and unrolling of loops):
            //

            //
            //  Bezier basis functions are denoted with B while the rational multipliers for the
            //  interior points will be denoted G -- so we have B(s), B(t) and G(s,t):
            //
            //  Directional Bezier basis functions B at s and t:
            float[] Bs = new float[4];
            float[] Bds = new float[4];
            float[] Bdss = new float[4];
            float[] Bt = new float[4];
            float[] Bdt = new float[4];
            float[] Bdtt = new float[4];

            evalBezierCurve(s, Bs, wDs != null ? Bds : null, wDss != null ? Bdss : null);
            evalBezierCurve(t, Bt, wDt != null ? Bdt : null, wDtt != null ? Bdtt : null);

            //  Rational multipliers G at s and t:
            float sC = 1.0f - s;
            float tC = 1.0f - t;

            //  Use <= here to avoid compiler warnings -- the sums should always be non-negative:
            float df0 = s  + t;   df0 = (df0 <= 0.0f) ? (float)1.0f : (1.0f / df0);
            float df1 = sC + t;   df1 = (df1 <= 0.0f) ? (float)1.0f : (1.0f / df1);
            float df2 = sC + tC;  df2 = (df2 <= 0.0f) ? (float)1.0f : (1.0f / df2);
            float df3 = s  + tC;  df3 = (df3 <= 0.0f) ? (float)1.0f : (1.0f / df3);

            //  Make sure the G[i] for pairs of interior points sum to 1 in all cases:
            float[] G = new float[8] { s*df0, (1.0f -  s*df0),
                t*df1, (1.0f -  t*df1),
                sC*df2, (1.0f - sC*df2),
                tC*df3, (1.0f - tC*df3) };

            //  Combined weights for boundary and interior points:
            for (int i = 0; i < 12; ++i)
            {
                point[boundaryGregory[i]] = Bs[boundaryBezSCol[i]] * Bt[boundaryBezTRow[i]];
            }
            for (int i = 0; i < 8; ++i)
            {
                point[interiorGregory[i]] = Bs[interiorBezSCol[i]] * Bt[interiorBezTRow[i]] * G[i];
            }

            //
            //  For derivatives, the basis functions for the interior points are rational and ideally
            //  require appropriate differentiation, i.e. product rule for the combination of B and G
            //  and the quotient rule for the rational G itself.  As initially proposed by Loop et al
            //  though, the approximation using the 16 Bezier points arising from the G(s,t) has
            //  proved adequate (and is what the GPU shaders use) so we continue to use that here.
            //
            //  An implementation of the true derivatives is provided and conditionally compiled for
            //  those that require it, e.g.:
            //
            //    dclyde's note: skipping half of the product rule like this does seem to change the
            //    result a lot in my tests.  This is not a runtime bottleneck for cloth sims anyway
            //    so I'm just using the accurate version.
            //
            if (wDs != null && wDt != null)
            {
                bool find_second_partials = wDs != null && wDst != null && wDtt != null;

                //  Combined weights for boundary points -- simple tensor products:
                for (int i = 0; i < 12; ++i)
                {
                    int iDst = boundaryGregory[i];
                    int tRow = boundaryBezTRow[i];
                    int sCol = boundaryBezSCol[i];

                    wDs[iDst] = Bds[sCol] * Bt[tRow];
                    wDt[iDst] = Bdt[tRow] * Bs[sCol];

                    if (find_second_partials)
                    {
                        wDss[iDst] = Bdss[sCol] * Bt[tRow];
                        wDst[iDst] = Bds[sCol] * Bdt[tRow];
                        wDtt[iDst] = Bs[sCol] * Bdtt[tRow];
                    }
                }

                //  Approximation to the true Gregory derivatives by differentiating the Bezier patch
                //  unique to the given (s,t), i.e. having F = (g^+ * f^+) + (g^- * f^-) as its four
                //  interior points:
                //
                //  Combined weights for interior points -- tensor products with G+ or G-:
                for (int i = 0; i < 8; ++i)
                {
                    int iDst = interiorGregory[i];
                    int tRow = interiorBezTRow[i];
                    int sCol = interiorBezSCol[i];

                    wDs[iDst] = Bds[sCol] * Bt[tRow] * G[i];
                    wDt[iDst] = Bdt[tRow] * Bs[sCol] * G[i];

                    if (find_second_partials)
                    {
                        wDss[iDst] = Bdss[sCol] * Bt[tRow] * G[i];
                        wDst[iDst] = Bds[sCol] * Bdt[tRow] * G[i];
                        wDtt[iDst] = Bs[sCol] * Bdtt[tRow] * G[i];
                    }
                }
            }
            return 20;
        }

        //
        //  Basis support for triangular patches:
        //
        //  Triangular patches may be evaluated in barycentric (trivariate) or
        //  bivariate form, depending on the complexity of their basis functions.
        //  The parametric orientation for a triangle is as follows:
        //
        //            (1,0)
        //              *
        //             . .
        //          t . 2 .
        //           .     .
        //          . 0   1 .
        //   (0,0) *---------* (1,0)
        //              s
        //
        //  With the origin (0,0) -- barycentric (0,0,w = 1) -- oriented at the
        //  corner V0, the corners V0, V1, and V2 correspond to barycentric
        //  coordinates W, U and V.  This is consistent with GPU tessellation
        //  shaders, but not with many publications where the corners correspond
        //  more intuitively to U, V and W.
        //

        //
        //  Simple linear triangle:
        //
        public static int EvalBasisLinearTri(float s, float t,
        float[] wP, float[] wDs = null, float[] wDt = null,
        float[] wDss = null, float[] wDst = null, float[] wDtt = null)
        {

            if (wP != null)
            {
                wP[0] = 1.0f - s - t;
                wP[1] = s;
                wP[2] = t;
            }
            if (wDs != null && wDt != null)
            {
                wDs[0] = -1.0f;
                wDs[1] =  1.0f;
                wDs[2] =  0.0f;

                wDt[0] = -1.0f;
                wDt[1] =  0.0f;
                wDt[2] =  1.0f;

                if (wDss != null && wDst != null && wDtt != null)
                {
                    wDss[0] = wDss[1] = wDss[2] = 0.0f;
                    wDst[0] = wDst[1] = wDst[2] = 0.0f;
                    wDtt[0] = wDtt[1] = wDtt[2] = 0.0f;
                }
            }
            return 3;
        }

        //
        //  Quartic Box spline triangle:
        //
        //  Points for the quartic triangular Box spline (representing regular
        //  patches for Loop subdivision) are as follows:
        //
        //         10-----11
        //         . .   . .
        //        .   . .   .
        //       7-----8-----9
        //      . .   . .   . .
        //     .   . .   . .   .
        //    3-----4-----5-----6
        //     .   . .   . .   .
        //      . .   . .   . /
        //       0-----1-----2
        //
        //  Stam provided the basis functions for these patches in terms of barycentric
        //  coordinates (u,v,w) (see Stam's "Evaluation of Loop Subdivision Surfaces").
        //  Unfortunately, unlike the basis functions for a quartic Bezier triangle,
        //  they are not very compact -- 3 functions involving 9 quartic terms and 3
        //  others involving 15 quartic terms.  (In contrast, the maximum number of
        //  terms in bivariate form is 15.)
        //
        //  Since we also need to differentiate with respect to u and v, we eliminate w
        //  and use the coefficient matrix C multiplied by the set of monomials M
        //  evaluated at (u,v), i.e. the full set of basis functions is:
        //
        //      B(u,v) = C * M(u,v)
        //
        //  where
        //
        //      M(u,v) = { 1, u,v, uu,uv,vv, uuu,uuv,uvv,vvv, uuuu,uuuv,uuvv,uvvv,vvvv }
        //
        //  and the 12 x 15 matrix C is as follows, scaled by a common factor of 1/12:
        //
        //      { 1, -2,-4,    0,  6,  6,   2,  0, -6, -4,  -1, -2, 0,  2,  1 },
        //      { 1,  2,-2,    0, -6,  0,  -4,  0,  6,  2,   2,  4, 0, -2, -1 },
        //      { 0,  0, 0,    0,  0,  0,   2,  0,  0,  0,  -1, -2, 0,  0,  0 },
        //      { 1, -4,-2,    6,  6,  0,  -4, -6,  0,  2,   1,  2, 0, -2, -1 },
        //      { 6,  0, 0,  -12,-12,-12,   8, 12, 12,  8,  -1, -2, 0, -2, -1 },
        //      { 1,  4, 2,    6,  6,  0,  -4, -6,-12, -4,  -1, -2, 0,  4,  2 },
        //      { 0,  0, 0,    0,  0,  0,   0,  0,  0,  0,   1,  2, 0,  0,  0 },
        //      { 1, -2, 2,    0, -6,  0,   2,  6,  0, -4,  -1, -2, 0,  4,  2 },
        //      { 1,  2, 4,    0,  6,  6,  -4,-12, -6, -4,   2,  4, 0, -2, -1 },
        //      { 0,  0, 0,    0,  0,  0,   2,  6,  6,  2,  -1, -2, 0, -2, -1 },
        //      { 0,  0, 0,    0,  0,  0,   0,  0,  0,  2,   0,  0, 0, -2, -1 },
        //      { 0,  0, 0,    0,  0,  0,   0,  0,  0,  0,   0,  0, 0,  2,  1 }
        //
        //  Differentiating the monomials and refactoring yields a unique set of
        //  coefficients for each of the derivatives, which we multiply by M(u,v).
        //
        public static void evalBivariateMonomialsQuartic(float s, float t, float[] M)
        {

            M[0] = 1;

            M[1] = s;
            M[2] = t;

            M[3] = s * s;
            M[4] = s * t;
            M[5] = t * t;

            M[6] = M[3] * s;
            M[7] = M[4] * s;
            M[8] = M[4] * t;
            M[9] = M[5] * t;

            M[10] = M[6] * s;
            M[11] = M[7] * s;
            M[12] = M[3] * M[5];
            M[13] = M[8] * t;
            M[14] = M[9] * t;
        }

        public static void evalBoxSplineTriDerivWeights(float[] stMonomials, int ds, int dt, float[] w)
        {

            float[] M = stMonomials;

            float S = 1;

            int totalOrder = ds + dt;
            if (totalOrder == 0)
            {
                S *= (float) (1.0 / 12.0);

                w[0]  = S * (1 - 2*M[1] - 4*M[2]          + 6*M[4] + 6*M[5] + 2*M[6]          - 6*M[8] - 4*M[9] -   M[10] - 2*M[11] + 2*M[13] +   M[14]);
                w[1]  = S * (1 + 2*M[1] - 2*M[2]          - 6*M[4]          - 4*M[6]          + 6*M[8] + 2*M[9] + 2*M[10] + 4*M[11] - 2*M[13] -   M[14]);
                w[2]  = S * (                                                 2*M[6]                            -   M[10] - 2*M[11]                    );
                w[3]  = S * (1 - 4*M[1] - 2*M[2] + 6*M[3] + 6*M[4]          - 4*M[6] - 6*M[7]          + 2*M[9] +   M[10] + 2*M[11] - 2*M[13] -   M[14]);
                w[4]  = S * (6                   -12*M[3] -12*M[4] -12*M[5] + 8*M[6] +12*M[7] +12*M[8] + 8*M[9] -   M[10] - 2*M[11] - 2*M[13] -   M[14]);
                w[5]  = S * (1 + 4*M[1] + 2*M[2] + 6*M[3] + 6*M[4]          - 4*M[6] - 6*M[7] -12*M[8] - 4*M[9] -   M[10] - 2*M[11] + 4*M[13] + 2*M[14]);
                w[6]  = S * (                                                                                       M[10] + 2*M[11]                    );
                w[7]  = S * (1 - 2*M[1] + 2*M[2]          - 6*M[4]          + 2*M[6] + 6*M[7]          - 4*M[9] -   M[10] - 2*M[11] + 4*M[13] + 2*M[14]);
                w[8]  = S * (1 + 2*M[1] + 4*M[2]          + 6*M[4] + 6*M[5] - 4*M[6] -12*M[7] - 6*M[8] - 4*M[9] + 2*M[10] + 4*M[11] - 2*M[13] -   M[14]);
                w[9]  = S * (                                                 2*M[6] + 6*M[7] + 6*M[8] + 2*M[9] -   M[10] - 2*M[11] - 2*M[13] -   M[14]);
                w[10] = S * (                                                                            2*M[9]                     - 2*M[13] -   M[14]);
                w[11] = S * (                                                                                                         2*M[13] +   M[14]);
            }
            else if (totalOrder == 1)
            {
                S *= (float) (1.0 / 6.0);

                if (ds != 0)
                {
                    w[0]  = S * (-1          + 3*M[2] + 3*M[3]          - 3*M[5] - 2*M[6] - 3*M[7] +   M[9]);
                    w[1]  = S * ( 1          - 3*M[2] - 6*M[3]          + 3*M[5] + 4*M[6] + 6*M[7] -   M[9]);
                    w[2]  = S * (                       3*M[3]                   - 2*M[6] - 3*M[7]         );
                    w[3]  = S * (-2 + 6*M[1] + 3*M[2] - 6*M[3] - 6*M[4]          + 2*M[6] + 3*M[7] -   M[9]);
                    w[4]  = S * (   -12*M[1] - 6*M[2] +12*M[3] +12*M[4] + 6*M[5] - 2*M[6] - 3*M[7] -   M[9]);
                    w[5]  = S * ( 2 + 6*M[1] + 3*M[2] - 6*M[3] - 6*M[4] - 6*M[5] - 2*M[6] - 3*M[7] + 2*M[9]);
                    w[6]  = S * (                                                  2*M[6] + 3*M[7]         );
                    w[7]  = S * (-1          - 3*M[2] + 3*M[3] + 6*M[4]          - 2*M[6] - 3*M[7] + 2*M[9]);
                    w[8]  = S * ( 1          + 3*M[2] - 6*M[3] -12*M[4] - 3*M[5] + 4*M[6] + 6*M[7] -   M[9]);
                    w[9]  = S * (                       3*M[3] + 6*M[4] + 3*M[5] - 2*M[6] - 3*M[7] -   M[9]);
                    w[10] = S * (                                                                  -   M[9]);
                    w[11] = S * (                                                                      M[9]);
                }
                else
                {
                    w[0]  = S * (-2 + 3*M[1] + 6*M[2]          - 6*M[4] - 6*M[5]  -   M[6] + 3*M[8] + 2*M[9]);
                    w[1]  = S * (-1 - 3*M[1]                   + 6*M[4] + 3*M[5]  + 2*M[6] - 3*M[8] - 2*M[9]);
                    w[2]  = S * (                                                 -   M[6]                  );
                    w[3]  = S * (-1 + 3*M[1]          - 3*M[3]          + 3*M[5]  +   M[6] - 3*M[8] - 2*M[9]);
                    w[4]  = S * (   - 6*M[1] -12*M[2] + 6*M[3] +12*M[4] +12*M[5]  -   M[6] - 3*M[8] - 2*M[9]);
                    w[5]  = S * ( 1 + 3*M[1]          - 3*M[3] -12*M[4] - 6*M[5]  -   M[6] + 6*M[8] + 4*M[9]);
                    w[6]  = S * (                                                 +   M[6]                  );
                    w[7]  = S * ( 1 - 3*M[1]          + 3*M[3]          - 6*M[5]  -   M[6] + 6*M[8] + 4*M[9]);
                    w[8]  = S * ( 2 + 3*M[1] + 6*M[2] - 6*M[3] - 6*M[4] - 6*M[5]  + 2*M[6] - 3*M[8] - 2*M[9]);
                    w[9]  = S * (                     + 3*M[3] + 6*M[4] + 3*M[5]  -   M[6] - 3*M[8] - 2*M[9]);
                    w[10] = S * (                                         3*M[5]           - 3*M[8] - 2*M[9]);
                    w[11] = S * (                                                            3*M[8] + 2*M[9]);
                }
            }
            else if (totalOrder == 2)
            {
                if (ds == 2)
                {
                    w[0]  = S * (   +   M[1]          -   M[3] -   M[4]);
                    w[1]  = S * (   - 2*M[1]          + 2*M[3] + 2*M[4]);
                    w[2]  = S * (       M[1]          -   M[3] -   M[4]);
                    w[3]  = S * ( 1 - 2*M[1] -   M[2] +   M[3] +   M[4]);
                    w[4]  = S * (-2 + 4*M[1] + 2*M[2] -   M[3] -   M[4]);
                    w[5]  = S * ( 1 - 2*M[1] -   M[2] -   M[3] -   M[4]);
                    w[6]  = S * (                         M[3] +   M[4]);
                    w[7]  = S * (   +   M[1] +   M[2] -   M[3] -   M[4]);
                    w[8]  = S * (   - 2*M[1] - 2*M[2] + 2*M[3] + 2*M[4]);
                    w[9]  = S * (       M[1] +   M[2] -   M[3] -   M[4]);
                    w[10] =     0;
                    w[11] =     0;
                }
                else if (dt == 2)
                {
                    w[0]  = S * ( 1 -   M[1] - 2*M[2] +   M[4] +   M[5]);
                    w[1]  = S * (   +   M[1] +   M[2] -   M[4] -   M[5]);
                    w[2]  =     0;
                    w[3]  = S * (            +   M[2] -   M[4] -   M[5]);
                    w[4]  = S * (-2 + 2*M[1] + 4*M[2] -   M[4] -   M[5]);
                    w[5]  = S * (   - 2*M[1] - 2*M[2] + 2*M[4] + 2*M[5]);
                    w[6]  =     0;
                    w[7]  = S * (            - 2*M[2] + 2*M[4] + 2*M[5]);
                    w[8]  = S * ( 1 -   M[1] - 2*M[2] -   M[4] -   M[5]);
                    w[9]  = S * (   +   M[1] +   M[2] -   M[4] -   M[5]);
                    w[10] = S * (                M[2] -   M[4] -   M[5]);
                    w[11] = S * (                         M[4] +   M[5]);
                }
                else
                {
                    S *= (float) (1.0 / 2.0);

                    w[0]  = S * ( 1          - 2*M[2] -   M[3] +   M[5]);
                    w[1]  = S * (-1          + 2*M[2] + 2*M[3] -   M[5]);
                    w[2]  = S * (                     -   M[3]         );
                    w[3]  = S * ( 1 - 2*M[1]          +   M[3] -   M[5]);
                    w[4]  = S * (-2 + 4*M[1] + 4*M[2] -   M[3] -   M[5]);
                    w[5]  = S * ( 1 - 2*M[1] - 4*M[2] -   M[3] + 2*M[5]);
                    w[6]  = S * (                     +   M[3]         );
                    w[7]  = S * (-1 + 2*M[1]          -   M[3] + 2*M[5]);
                    w[8]  = S * ( 1 - 4*M[1] - 2*M[2] + 2*M[3] -   M[5]);
                    w[9]  = S * (   + 2*M[1] + 2*M[2] -   M[3] -   M[5]);
                    w[10] = S * (                              -   M[5]);
                    w[11] = S * (                                  M[5]);
                }
            }
            else
            {
                System.Diagnostics.Debug.Assert(totalOrder <= 2);
            }
        }

        public static void adjustBoxSplineTriBoundaryWeights(int boundaryMask, float[] weights)
        {

            if (boundaryMask == 0) return;

            //
            //  Determine boundary edges and vertices from the lower 3 and upper
            //  2 bits of the 5-bit mask:
            //
            int upperBits = (boundaryMask >> 3) & 0x3;
            int lowerBits = boundaryMask & 7;

            int eBits = lowerBits;
            int vBits = 0;

            if (upperBits == 1)
            {
                //  Boundary vertices only:
                vBits = eBits;
                eBits = 0;
            }
            else if (upperBits == 2)
            {
                //  Opposite vertex bit is edge bit rotated one to the right:
                vBits = ((eBits & 1) << 2) | (eBits >> 1);
            }

            bool edge0IsBoundary = (eBits & 1) != 0;
            bool edge1IsBoundary = (eBits & 2) != 0;
            bool edge2IsBoundary = (eBits & 4) != 0;

            //
            //  Adjust weights for the 4 boundary points and 3 interior points
            //  to account for the 3 phantom points adjacent to each
            //  boundary edge:
            //
            if (edge0IsBoundary)
            {
                float w0 = weights[0];
                if (edge2IsBoundary)
                {
                    //  P0 = B1 + (B1 - I1)
                    weights[4] += w0;
                    weights[4] += w0;
                    weights[8] -= w0;
                }
                else
                {
                    //  P0 = B1 + (B0 - I0)
                    weights[4] += w0;
                    weights[3] += w0;
                    weights[7] -= w0;
                }

                //  P1 = B1 + (B2 - I1)
                float w1 = weights[1];
                weights[4] += w1;
                weights[5] += w1;
                weights[8] -= w1;

                float w2 = weights[2];
                if (edge1IsBoundary)
                {
                    //  P2 = B2 + (B2 - I1)
                    weights[5] += w2;
                    weights[5] += w2;
                    weights[8] -= w2;
                }
                else
                {
                    //  P2 = B2 + (B3 - I2)
                    weights[5] += w2;
                    weights[6] += w2;
                    weights[9] -= w2;
                }
                //  Clear weights for the phantom points:
                weights[0] = weights[1] = weights[2] = 0.0f;
            }
            if (edge1IsBoundary)
            {
                float w0 = weights[6];
                if (edge0IsBoundary)
                {
                    //  P0 = B1 + (B1 - I1)
                    weights[5] += w0;
                    weights[5] += w0;
                    weights[4] -= w0;
                }
                else
                {
                    //  P0 = B1 + (B0 - I0)
                    weights[5] += w0;
                    weights[2] += w0;
                    weights[1] -= w0;
                }

                //  P1 = B1 + (B2 - I1)
                float w1 = weights[9];
                weights[5] += w1;
                weights[8] += w1;
                weights[4] -= w1;

                float w2 = weights[11];
                if (edge2IsBoundary)
                {
                    //  P2 = B2 + (B2 - I1)
                    weights[8] += w2;
                    weights[8] += w2;
                    weights[4] -= w2;
                }
                else
                {
                    //  P2 = B2 + (B3 - I2)
                    weights[8]  += w2;
                    weights[10] += w2;
                    weights[7]  -= w2;
                }
                //  Clear weights for the phantom points:
                weights[6] = weights[9] = weights[11] = 0.0f;
            }
            if (edge2IsBoundary)
            {
                float w0 = weights[10];
                if (edge1IsBoundary)
                {
                    //  P0 = B1 + (B1 - I1)
                    weights[8] += w0;
                    weights[8] += w0;
                    weights[5] -= w0;
                }
                else
                {
                    //  P0 = B1 + (B0 - I0)
                    weights[8]  += w0;
                    weights[11] += w0;
                    weights[9]  -= w0;
                }

                //  P1 = B1 + (B2 - I1)
                float w1 = weights[7];
                weights[8] += w1;
                weights[4] += w1;
                weights[5] -= w1;

                float w2 = weights[3];
                if (edge0IsBoundary)
                {
                    //  P2 = B2 + (B2 - I1)
                    weights[4] += w2;
                    weights[4] += w2;
                    weights[5] -= w2;
                }
                else
                {
                    //  P2 = B2 + (B3 - I2)
                    weights[4] += w2;
                    weights[0] += w2;
                    weights[1] -= w2;
                }
                //  Clear weights for the phantom points:
                weights[10] = weights[7] = weights[3] = 0.0f;
            }

            //
            //  Adjust weights for the 3 boundary points and the 2 interior
            //  points to account for the 2 phantom points adjacent to
            //  each boundary vertex:
            //
            if ((vBits & 1) != 0)
            {
                //  P0 = B1 + (B0 - I0)
                float w0 = weights[3];
                weights[4] += w0;
                weights[7] += w0;
                weights[8] -= w0;

                //  P1 = B1 + (B2 - I1)
                float w1 = weights[0];
                weights[4] += w1;
                weights[1] += w1;
                weights[5] -= w1;

                //  Clear weights for the phantom points:
                weights[3] = weights[0] = 0.0f;
            }
            if ((vBits & 2) != 0)
            {
                //  P0 = B1 + (B0 - I0)
                float w0 = weights[2];
                weights[5] += w0;
                weights[1] += w0;
                weights[4] -= w0;

                //  P1 = B1 + (B2 - I1)
                float w1 = weights[6];
                weights[5] += w1;
                weights[9] += w1;
                weights[8] -= w1;

                //  Clear weights for the phantom points:
                weights[2] = weights[6] = 0.0f;
            }
            if ((vBits & 4) != 0)
            {
                //  P0 = B1 + (B0 - I0)
                float w0 = weights[11];
                weights[8] += w0;
                weights[9] += w0;
                weights[5] -= w0;

                //  P1 = B1 + (B2 - I1)
                float w1 = weights[10];
                weights[8] += w1;
                weights[7] += w1;
                weights[4] -= w1;

                //  Clear weights for the phantom points:
                weights[11] = weights[10] = 0.0f;
            }
        }

        public static void boundBasisBoxSplineTri(int boundary,
        float[] wP, float[] wDs, float[] wDt,
        float[] wDss, float[] wDst, float[] wDtt)
        {

            if (wP != null)
            {
                adjustBoxSplineTriBoundaryWeights(boundary, wP);
            }
            if (wDs != null && wDt != null)
            {
                adjustBoxSplineTriBoundaryWeights(boundary, wDs);
                adjustBoxSplineTriBoundaryWeights(boundary, wDt);

                if (wDss != null && wDst != null && wDtt != null)
                {
                    adjustBoxSplineTriBoundaryWeights(boundary, wDss);
                    adjustBoxSplineTriBoundaryWeights(boundary, wDst);
                    adjustBoxSplineTriBoundaryWeights(boundary, wDtt);
                }
            }
        }

        public static int EvalBasisBoxSplineTri(float s, float t,
        float[] wP, float[] wDs = null, float[] wDt = null,
        float[] wDss = null, float[] wDst = null, float[] wDtt = null)
        {

            float[] stMonomials = new float[15];
            evalBivariateMonomialsQuartic(s, t, stMonomials);

            if (wP != null)
            {
                evalBoxSplineTriDerivWeights(stMonomials, 0, 0, wP);
            }
            if (wDs != null && wDt != null)
            {
                evalBoxSplineTriDerivWeights(stMonomials, 1, 0, wDs);
                evalBoxSplineTriDerivWeights(stMonomials, 0, 1, wDt);

                if (wDss != null && wDst != null && wDtt != null)
                {
                    evalBoxSplineTriDerivWeights(stMonomials, 2, 0, wDss);
                    evalBoxSplineTriDerivWeights(stMonomials, 1, 1, wDst);
                    evalBoxSplineTriDerivWeights(stMonomials, 0, 2, wDtt);
                }
            }
            return 12;
        }

        //
        //  Quartic Bezier triangle:
        //
        //  The regular patch for Loop subdivision is a quartic triangular Box spline
        //  with cubic boundaries.  So we need a quartic Bezier patch to represent it
        //  faithfully.
        //
        //  The formulae for the 15 quartic basis functions are:
        //
        //                       4!        i   j   k
        //      B   (u,v,w) =  ------- * (u * v * w )
        //       ijk           i!j!k!
        //
        //  for each i + j + k = 4.   The control points (P) and correspondingly
        //  labeled p<i,j,k> are oriented as follows:
        //
        //                    P14                                   p040
        //                P12     P13                           p031    p130
        //            P9      P10     P11                   p022    p121    p220
        //        P5      P6      P7      P8            p013    p112    p211    p310
        //    P0      P1      P2      P3      P4    p004    p103    p202    p301    p400
        //
        public static void evalBezierTriDerivWeights(float s, float t, int ds, int dt, float[] wB)
        {

            float u  = s;
            float v  = t;
            float w  = 1 - u - v;

            float uu = u * u;
            float vv = v * v;
            float ww = w * w;

            float uv = u * v;
            float vw = v * w;
            float uw = u * w;

            int totalOrder = ds + dt;
            if (totalOrder == 0)
            {
                wB[0]  =      ww * ww;
                wB[1]  =  4 * uw * ww;
                wB[2]  =  6 * uw * uw;
                wB[3]  =  4 * uw * uu;
                wB[4]  =      uu * uu;
                wB[5]  =  4 * vw * ww;
                wB[6]  = 12 * ww * uv;
                wB[7]  = 12 * uu * vw;
                wB[8]  =  4 * uv * uu;
                wB[9]  =  6 * vw * vw;
                wB[10] = 12 * vv * uw;
                wB[11] =  6 * uv * uv;
                wB[12] =  4 * vw * vv;
                wB[13] =  4 * uv * vv;
                wB[14] =      vv * vv;
            }
            else if (totalOrder == 1)
            {
                if (ds == 1)
                {
                    wB[0]  =  -4 * ww * w;
                    wB[1]  =   4 * ww * (w - 3 * u);
                    wB[2]  =  12 * uw * (w - u);
                    wB[3]  =   4 * uu * (3 * w - u);
                    wB[4]  =   4 * uu * u;
                    wB[5]  = -12 * vw * w;
                    wB[6]  =  12 * vw * (w - 2 * u);
                    wB[7]  =  12 * uv * (2 * w - u);
                    wB[8]  =  12 * uv * u;
                    wB[9]  = -12 * vv * w;
                    wB[10] =  12 * vv * (w - u);
                    wB[11] =  12 * vv * u;
                    wB[12] =  -4 * vv * v;
                    wB[13] =   4 * vv * v;
                    wB[14] =   0;
                }
                else
                {
                    wB[0]  =  -4 * ww * w;
                    wB[1]  = -12 * ww * u;
                    wB[2]  = -12 * uu * w;
                    wB[3]  =  -4 * uu * u;
                    wB[4]  =   0;
                    wB[5]  =   4 * ww * (w - 3 * v);
                    wB[6]  =  12 * uw * (w - 2 * v);
                    wB[7]  =  12 * uu * (w - v);
                    wB[8]  =   4 * uu * u;
                    wB[9]  =  12 * vw * (w - v);
                    wB[10] =  12 * uv * (2 * w - v);
                    wB[11] =  12 * uv * u;;
                    wB[12] =   4 * vv * (3 * w - v);
                    wB[13] =  12 * vv * u;
                    wB[14] =   4 * vv * v;
                }
            }
            else if (totalOrder == 2)
            {
                if (ds == 2)
                {
                    wB[0]  =  12 * ww;
                    wB[1]  =  24 * (uw - ww);
                    wB[2]  =  12 * (uu - 4 * uw + ww);
                    wB[3]  =  24 * (uw - uu);
                    wB[4]  =  12 * uu;
                    wB[5]  =  24 * vw;
                    wB[6]  =  24 * (uv - 2 * vw);
                    wB[7]  =  24 * (vw - 2 * uv);
                    wB[8]  =  24 * uv;
                    wB[9]  =  12 * vv;
                    wB[10] = -24 * vv;
                    wB[11] =  12 * vv;
                    wB[12] =   0;
                    wB[13] =   0;
                    wB[14] =   0;
                }
                else if (dt == 2)
                {
                    wB[0]  =  12 * ww;
                    wB[1]  =  24 * uw;
                    wB[2]  =  12 * uu;
                    wB[3]  =   0;
                    wB[4]  =   0;
                    wB[5]  =  24 * (vw - ww);
                    wB[6]  =  24 * (uv - 2 * uw);
                    wB[7]  = -24 * uu;
                    wB[8]  =   0;
                    wB[9]  =  12 * (vv - 4 * vw + ww);
                    wB[10] =  24 * (uw - 2 * uv);
                    wB[11] =  12 * uu;
                    wB[12] =  24 * (vw - vv);
                    wB[13] =  24 * uv;
                    wB[14] =  12 * vv;
                }
                else
                {
                    wB[0]  =  12 * ww;
                    wB[3]  = -12 * uu;
                    wB[13] =  12 * vv;
                    wB[11] =  24 * uv;
                    wB[1]  =  24 * uw - wB[0];
                    wB[2]  = -24 * uw - wB[3];
                    wB[5]  =  24 * vw - wB[0];
                    wB[6]  = -24 * vw + wB[11] - wB[1];
                    wB[8]  = - wB[3];
                    wB[7]  = -(wB[11] + wB[2]);
                    wB[9]  =   wB[13] - wB[5] - wB[0];
                    wB[10] = -(wB[9] + wB[11]);
                    wB[12] = - wB[13];
                    wB[4]  =   0;
                    wB[14] =   0;
                }
            }
            else
            {
                System.Diagnostics.Debug.Assert(totalOrder <= 2);
            }
        }

        public static int EvalBasisBezierTri(float s, float t,
        float[] wP, float[] wDs = null, float[] wDt = null,
        float[] wDss = null, float[] wDst = null, float[] wDtt = null)
        {

            if (wP != null)
            {
                evalBezierTriDerivWeights(s, t, 0, 0, wP);
            }
            if (wDs != null && wDt != null)
            {
                evalBezierTriDerivWeights(s, t, 1, 0, wDs);
                evalBezierTriDerivWeights(s, t, 0, 1, wDt);

                if (wDss != null && wDst != null && wDtt != null)
                {
                    evalBezierTriDerivWeights(s, t, 2, 0, wDss);
                    evalBezierTriDerivWeights(s, t, 1, 1, wDst);
                    evalBezierTriDerivWeights(s, t, 0, 2, wDtt);
                }
            }
            return 15;
        }

        //
        //  Quartic Gregory triangle:
        //
        //  The 18-point quaritic Gregory patch is an extension of the 15-point
        //  quartic Bezier triangle with the 3 interior points of the Bezier patch
        //  replaced with pairs of points (face points -- fi+ and fi-) that are
        //  rationally combined.
        //
        //  The point ordering of Gregory patches deviates considerably from the
        //  BSpline and Bezier patches by grouping the 5 points at each corner and
        //  ordering the groups by corner index.  This is consistent with the cubic
        //  Gregory quad patch.
        //
        //  The 3 additional quartic boundary points are currently appended to these
        //  3 groups of 5 control points.  In contrast to the 5 points associated
        //  with each corner, these 3 points are more associated with the edge
        //  between the corner vertices and are equally weighted between the two.
        //
        //
        //  Expanding a set of 15 Bezier basis functions for the 6 (3 pairs) of
        //  rational weights for the 18 Gregory basis functions:
        //
        public static void convertBezierWeightsToGregory(float[] wB, float[] rG, float[] wG)
        {

            wG[0]  = wB[0];
            wG[1]  = wB[1];
            wG[2]  = wB[5];
            wG[3]  = wB[6] * rG[0];
            wG[4]  = wB[6] * rG[1];

            wG[5]  = wB[4];
            wG[6]  = wB[8];
            wG[7]  = wB[3];
            wG[8]  = wB[7] * rG[2];
            wG[9]  = wB[7] * rG[3];

            wG[10] = wB[14];
            wG[11] = wB[12];
            wG[12] = wB[13];
            wG[13] = wB[10] * rG[4];
            wG[14] = wB[10] * rG[5];

            wG[15] = wB[2];
            wG[16] = wB[11];
            wG[17] = wB[9];
        }

        public static int EvalBasisGregoryTri(float s, float t,
        float[] wP, float[] wDs = null, float[] wDt = null,
        float[] wDss = null, float[] wDst = null, float[] wDtt = null)
        {

            //
            //  Bezier basis functions are denoted with B while the rational multipliers for the
            //  interior points will be denoted G -- so we have B(s,t) and G(s,t) (though we
            //  switch to barycentric (u,v,w) briefly to compute G)
            //
            float[] BP = new float[15];
            float[] BDs = new float[15];
            float[] BDt = new float[15];
            float[] BDss = new float[15];
            float[] BDst = new float[15];
            float[] BDtt = new float[15];

            float[] G = new float[6] { 1.0f, 0.0f, 1.0f, 0.0f, 1.0f, 0.0f };
            float u = s;
            float v = t;
            float w = 1 - u - v;

            if ((u + v) > 0)
            {
                G[0]  = u / (u + v);
                G[1]  = v / (u + v);
            }
            if ((v + w) > 0)
            {
                G[2] = v / (v + w);
                G[3] = w / (v + w);
            }
            if ((w + u) > 0)
            {
                G[4] = w / (w + u);
                G[5] = u / (w + u);
            }

            //
            //  Compute Bezier basis functions and convert, adjusting interior points:
            //
            if (wP != null)
            {
                evalBezierTriDerivWeights(s, t, 0, 0, BP);
                convertBezierWeightsToGregory(BP, G, wP);
            }
            if (wDs != null && wDt != null)
            {
                //  TBD -- ifdef OPENSUBDIV_GREGORY_EVAL_TRUE_DERIVATIVES

                evalBezierTriDerivWeights(s, t, 1, 0, BDs);
                evalBezierTriDerivWeights(s, t, 0, 1, BDt);

                convertBezierWeightsToGregory(BDs, G, wDs);
                convertBezierWeightsToGregory(BDt, G, wDt);

                if (wDss != null && wDst != null && wDtt != null)
                {
                    evalBezierTriDerivWeights(s, t, 2, 0, BDss);
                    evalBezierTriDerivWeights(s, t, 1, 1, BDst);
                    evalBezierTriDerivWeights(s, t, 0, 2, BDtt);

                    convertBezierWeightsToGregory(BDss, G, wDss);
                    convertBezierWeightsToGregory(BDst, G, wDst);
                    convertBezierWeightsToGregory(BDtt, G, wDtt);
                }
            }
            return 18;
        }

        //
        //  Higher level basis evaluation functions that deal with parameterization and
        //  boundary issues (reflected in PatchParam) for all patch types:
        //
        public static int EvaluatePatchBasisNormalized(int patchType, PatchParam param, float s, float t,
        float[] wP, float[] wDs = null, float[] wDt = null,
        float[] wDss = null, float[] wDst = null, float[] wDtt = null)
        {

            int boundaryMask = param.GetBoundary();

            int nPoints = 0;
            if (patchType == (int)PatchDescriptor.Type.REGULAR)
            {
                nPoints = EvalBasisBSpline(s, t, wP, wDs, wDt, wDss, wDst, wDtt);
                if (boundaryMask != 0)
                {
                    boundBasisBSpline(boundaryMask, wP, wDs, wDt, wDss, wDst, wDtt);
                }
            }
            else if (patchType == (int)PatchDescriptor.Type.LOOP)
            {
                nPoints = EvalBasisBoxSplineTri(s, t, wP, wDs, wDt, wDss, wDst, wDtt);
                if (boundaryMask != 0)
                {
                    boundBasisBoxSplineTri(boundaryMask, wP, wDs, wDt, wDss, wDst, wDtt);
                }
            }
            else if (patchType == (int)PatchDescriptor.Type.GREGORY_BASIS)
            {
                nPoints = EvalBasisGregory(s, t, wP, wDs, wDt, wDss, wDst, wDtt);
            }
            else if (patchType == (int)PatchDescriptor.Type.GREGORY_TRIANGLE)
            {
                nPoints = EvalBasisGregoryTri(s, t, wP, wDs, wDt, wDss, wDst, wDtt);
            }
            else if (patchType == (int)PatchDescriptor.Type.QUADS)
            {
                nPoints = EvalBasisLinear(s, t, wP, wDs, wDt, wDss, wDst, wDtt);
            }
            else if (patchType == (int)PatchDescriptor.Type.TRIANGLES)
            {
                nPoints = EvalBasisLinearTri(s, t, wP, wDs, wDt, wDss, wDst, wDtt);
            }
            else
            {
                System.Diagnostics.Debug.Assert(false);
            }
            return nPoints;
        }

        public static int EvaluatePatchBasis(int patchType, PatchParam param, float s, float t,
        float[] wP, float[] wDs = null, float[] wDt = null,
        float[] wDss = null, float[] wDst = null, float[] wDtt = null)
        {

            float derivSign = 1.0f;

            if ((patchType == (int)PatchDescriptor.Type.LOOP) ||
            (patchType == (int)PatchDescriptor.Type.GREGORY_TRIANGLE) ||
            (patchType == (int)PatchDescriptor.Type.TRIANGLES))
            {
                param.NormalizeTriangle(ref s, ref t);
                if (param.IsTriangleRotated())
                {
                    derivSign = -1.0f;
                }
            }
            else
            {
                param.Normalize(ref s, ref t);
            }

            int nPoints = EvaluatePatchBasisNormalized(
            patchType, param, s, t, wP, wDs, wDt, wDss, wDst, wDtt);

            if (wDs != null && wDt != null)
            {
                float d1Scale = derivSign * (float)(1 << param.GetDepth());

                for (int i = 0; i < nPoints; ++i)
                {
                    wDs[i] *= d1Scale;
                    wDt[i] *= d1Scale;
                }

                if (wDss != null && wDst != null && wDtt != null)
                {
                    float d2Scale = derivSign * d1Scale * d1Scale;

                    for (int i = 0; i < nPoints; ++i)
                    {
                        wDss[i] *= d2Scale;
                        wDst[i] *= d2Scale;
                        wDtt[i] *= d2Scale;
                    }
                }
            }
            return nPoints;
        }

        //
        //  Explicit float and double instantiations:
        //
        //
        //  double versions
        //


        //
        //  Basis support for quadrilateral patches:
        //
        //  Quadrilateral patches are parameterized in terms of (s,t) as follows:
        //
        //      (1,0) *---------* (1,1)
        //            | 3     2 |
        //          t |         |
        //            |         |
        //            | 0     1 |
        //      (0,0) *---------* (1,0)
        //                 s
        //

        //
        //  Simple bilinear quad:
        //
        public static int EvalBasisLinear(double s, double t,
        double[] wP, double[] wDs = null, double[] wDt = null,
        double[] wDss = null, double[] wDst = null, double[] wDtt = null)
        {

            double sC = 1.0f - s;
            double tC = 1.0f - t;

            if (wP != null)
            {
                wP[0] = sC * tC;
                wP[1] =  s * tC;
                wP[2] =  s * t;
                wP[3] = sC * t;
            }
            if (wDs != null && wDt != null)
            {
                wDs[0] = -tC;
                wDs[1] =  tC;
                wDs[2] =   t;
                wDs[3] =  -t;

                wDt[0] = -sC;
                wDt[1] =  -s;
                wDt[2] =   s;
                wDt[3] =  sC;

                if (wDss != null && wDst != null && wDtt != null)
                {
                    for(int i = 0; i < 4; ++i)
                    {
                        wDss[i] = 0.0f;
                        wDtt[i] = 0.0f;
                    }

                    wDst[0] =  1.0f;
                    wDst[1] = -1.0f;
                    wDst[2] =  1.0f;
                    wDst[3] = -1.0f;
                }
            }
            return 4;
        }

        //
        //  Bicubic BSpline patch:
        //
        //     12-----13------14-----15
        //      |      |      |      |
        //      |      |      |      |
        //      8------9------10-----11
        //      |      | t    |      |
        //      |      |   s  |      |
        //      4------5------6------7
        //      |      |      |      |
        //      |      |      |      |
        //      O------1------2------3
        //
        //  The basis if a bicubic BSpline patch is a tensor product, which we make
        //  use of here by evaluating, differentiating and combining basis functions
        //  in each of the two parametric directions.
        //
        //  Not all 16 points will be present.  The boundary mask indicates boundary
        //  edges beyond which phantom points are implicitly extrapolated.  Weights
        //  for missing points are set to zero while those contributing to their
        //  implicit extrapolation will be adjusted.
        //
        //
        //  Cubic BSpline curve basis evaluation:
        //
        public static void evalBSplineCurve(double t, double[] wP, double[] wDP, double[] wDP2)
        {

            double one6th = (double)(1.0 / 6.0);

            double t2 = t * t;
            double t3 = t * t2;

            wP[0] = one6th * (1.0f - 3.0f*(t -      t2) -      t3);
            wP[1] = one6th * (4.0f           - 6.0f*t2  + 3.0f*t3);
            wP[2] = one6th * (1.0f + 3.0f*(t +      t2  -      t3));
            wP[3] = one6th * (                                 t3);

            if (wDP != null)
            {
                wDP[0] = -0.5f*t2 +      t - 0.5f;
                wDP[1] =  1.5f*t2 - 2.0f*t;
                wDP[2] = -1.5f*t2 +      t + 0.5f;
                wDP[3] =  0.5f*t2;
            }
            if (wDP2 != null)
            {
                wDP2[0] = -       t + 1.0f;
                wDP2[1] =  3.0f * t - 2.0f;
                wDP2[2] = -3.0f * t + 1.0f;
                wDP2[3] =         t;
            }
        }

        //
        //  Weight adjustments to account for phantom end points:
        //
        public static void adjustBSplineBoundaryWeights(int boundary, double[] w)
        {

            if ((boundary & 1) != 0)
            {
                for (int i = 0; i < 4; ++i)
                {
                    w[i + 8] -= w[i + 0];
                    w[i + 4] += w[i + 0] * 2.0f;
                    w[i + 0]  = 0.0f;
                }
            }
            if ((boundary & 2) != 0)
            {
                for (int i = 0; i < 16; i += 4)
                {
                    w[i + 1] -= w[i + 3];
                    w[i + 2] += w[i + 3] * 2.0f;
                    w[i + 3]  = 0.0f;
                }
            }
            if ((boundary & 4) != 0)
            {
                for (int i = 0; i < 4; ++i)
                {
                    w[i +  4] -= w[i + 12];
                    w[i +  8] += w[i + 12] * 2.0f;
                    w[i + 12]  = 0.0f;
                }
            }
            if ((boundary & 8) != 0)
            {
                for (int i = 0; i < 16; i += 4)
                {
                    w[i + 2] -= w[i + 0];
                    w[i + 1] += w[i + 0] * 2.0f;
                    w[i + 0]  = 0.0f;
                }
            }
        }

        public static void boundBasisBSpline(int boundary,
        double[] wP, double[] wDs, double[] wDt,
        double[] wDss, double[] wDst, double[] wDtt)
        {

            if (wP != null)
            {
                adjustBSplineBoundaryWeights(boundary, wP);
            }
            if (wDs != null && wDt != null)
            {
                adjustBSplineBoundaryWeights(boundary, wDs);
                adjustBSplineBoundaryWeights(boundary, wDt);

                if (wDss != null && wDst != null && wDtt != null)
                {
                    adjustBSplineBoundaryWeights(boundary, wDss);
                    adjustBSplineBoundaryWeights(boundary, wDst);
                    adjustBSplineBoundaryWeights(boundary, wDtt);
                }
            }
        }

        public static int EvalBasisBSpline(double s, double t,
        double[] wP, double[] wDs = null, double[] wDt = null,
        double[] wDss = null, double[] wDst = null, double[] wDtt = null)
        {

            double[] sWeights = new double[4];
            double[] tWeights = new double[4];
            double[] dsWeights = new double[4];
            double[] dtWeights = new double[4];
            double[] dssWeights = new double[4];
            double[] dttWeights = new double[4];

            evalBSplineCurve(s, wP != null ? sWeights : null, wDs != null ? dsWeights : null, wDss != null ? dssWeights : null);
            evalBSplineCurve(t, wP != null ? tWeights : null, wDt != null ? dtWeights : null, wDtt != null ? dttWeights : null);

            if (wP != null)
            {
                for (int i = 0; i < 4; ++i)
                {
                    for (int j = 0; j < 4; ++j)
                    {
                        wP[4*i+j] = sWeights[j] * tWeights[i];
                    }
                }
            }
            if (wDs != null && wDt != null)
            {
                for (int i = 0; i < 4; ++i)
                {
                    for (int j = 0; j < 4; ++j)
                    {
                        wDs[4*i+j] = dsWeights[j] * tWeights[i];
                        wDt[4*i+j] = sWeights[j] * dtWeights[i];
                    }
                }

                if (wDss != null && wDst != null && wDtt != null)
                {
                    for (int i = 0; i < 4; ++i)
                    {
                        for (int j = 0; j < 4; ++j)
                        {
                            wDss[4*i+j] = dssWeights[j] * tWeights[i];
                            wDst[4*i+j] = dsWeights[j] * dtWeights[i];
                            wDtt[4*i+j] = sWeights[j] * dttWeights[i];
                        }
                    }
                }
            }
            return 16;
        }

        //
        //  Bicubic Bezier patch:
        //
        //     12-----13------14-----15
        //      |      |      |      |
        //      |      |      |      |
        //      8------9------10-----11
        //      |      |      |      |
        //      |      |      |      |
        //      4------5------6------7
        //      | t    |      |      |
        //      |   s  |      |      |
        //      O------1------2------3
        //
        //  As was the case with the BSpline patch, a bicubic Bezier patch can also
        //  make use of its tensor product property by evaluating, differentiating
        //  and combining basis functions in each of the two parametric directions.
        //
        //
        //  Cubic Bezier curve basis evaluation:
        //
        public static void evalBezierCurve(double t, double[] wP, double[] wDP, double[] wDP2)
        {

            // The four uniform cubic Bezier basis functions (in terms of t and its
            // complement tC) evaluated at t:
            double t2 = t*t;
            double tC = 1.0f - t;
            double tC2 = tC * tC;

            wP[0] = tC2 * tC;
            wP[1] = tC2 * t * 3.0f;
            wP[2] = t2 * tC * 3.0f;
            wP[3] = t2 * t;

            // Derivatives of the above four basis functions at t:
            if (wDP != null)
            {
                wDP[0] = -3.0f * tC2;
                wDP[1] =  9.0f * t2 - 12.0f * t + 3.0f;
                wDP[2] = -9.0f * t2 +  6.0f * t;
                wDP[3] =  3.0f * t2;
            }

            // Second derivatives of the basis functions at t:
            if (wDP2 != null)
            {
                wDP2[0] =   6.0f * tC;
                wDP2[1] =  18.0f * t - 12.0f;
                wDP2[2] = -18.0f * t +  6.0f;
                wDP2[3] =   6.0f * t;
            }
        }

        public static int EvalBasisBezier(double s, double t,
        double[] wP, double[] wDs = null, double[] wDt = null,
        double[] wDss = null, double[] wDst = null, double[] wDtt = null)
        {

            double[] sWeights = new double[4];
            double[] tWeights = new double[4];
            double[] dsWeights = new double[4];
            double[] dtWeights = new double[4];
            double[] dssWeights = new double[4];
            double[] dttWeights = new double[4];

            evalBezierCurve(s, wP != null ? sWeights : null, wDs != null ? dsWeights : null, wDss != null ? dssWeights : null);
            evalBezierCurve(t, wP != null ? tWeights : null, wDt != null ? dtWeights : null, wDtt != null ? dttWeights : null);

            if (wP != null)
            {
                for (int i = 0; i < 4; ++i)
                {
                    for (int j = 0; j < 4; ++j)
                    {
                        wP[4*i+j] = sWeights[j] * tWeights[i];
                    }
                }
            }
            if (wDs != null && wDt != null)
            {
                for (int i = 0; i < 4; ++i)
                {
                    for (int j = 0; j < 4; ++j)
                    {
                        wDs[4*i+j] = dsWeights[j] * tWeights[i];
                        wDt[4*i+j] = sWeights[j] * dtWeights[i];
                    }
                }

                if (wDss != null && wDst != null && wDtt != null)
                {
                    for (int i = 0; i < 4; ++i)
                    {
                        for (int j = 0; j < 4; ++j)
                        {
                            wDss[4*i+j] = dssWeights[j] * tWeights[i];
                            wDst[4*i+j] = dsWeights[j] * dtWeights[i];
                            wDtt[4*i+j] = sWeights[j] * dttWeights[i];
                        }
                    }
                }
            }
            return 16;
        }

        //
        //  Cubic Gregory patch:
        //
        //      P3         e3-      e2+         P2
        //         15------17-------11--------10
        //         |        |        |        |
        //         |        |        |        |
        //         |        | f3-    | f2+    |
        //         |       19       13        |
        //     e3+ 16-----18           14-----12 e2-
        //         |     f3+          f2-     |
        //         |                          |
        //         |                          |
        //         |      f0-         f1+     |
        //     e0- 2------4            8------6 e1+
        //         |        3        9        |
        //         |        | f0+    | f1-    |
        //         | t      |        |        |
        //         |   s    |        |        |
        //         O--------1--------7--------5
        //      P0         e0+      e1-         P1
        //
        //  The 20-point cubic Gregory patch is an extension of the 16-point bicubic
        //  Bezier patch with the 4 interior points of the Bezier patch replaced with
        //  pairs of points (face points -- fi+ and fi-) that are rationally combined.
        //
        //  The point ordering of the Gregory patch deviates considerably from the
        //  BSpline and Bezier patches by grouping the 5 points at each corner and
        //  ordering the groups by corner index.
        //
        public static int EvalBasisGregory(double s, double t,
        double[] point, double[] wDs = null, double[] wDt = null,
        double[] wDss = null, double[] wDst = null, double[] wDtt = null)
        {

            //  Indices of boundary and interior points and their corresponding Bezier points
            //  (this can be reduced with more direct indexing and unrolling of loops):
            //

            //
            //  Bezier basis functions are denoted with B while the rational multipliers for the
            //  interior points will be denoted G -- so we have B(s), B(t) and G(s,t):
            //
            //  Directional Bezier basis functions B at s and t:
            double[] Bs = new double[4];
            double[] Bds = new double[4];
            double[] Bdss = new double[4];
            double[] Bt = new double[4];
            double[] Bdt = new double[4];
            double[] Bdtt = new double[4];

            evalBezierCurve(s, Bs, wDs != null ? Bds : null, wDss != null ? Bdss : null);
            evalBezierCurve(t, Bt, wDt != null ? Bdt : null, wDtt != null ? Bdtt : null);

            //  Rational multipliers G at s and t:
            double sC = 1.0f - s;
            double tC = 1.0f - t;

            //  Use <= here to avoid compiler warnings -- the sums should always be non-negative:
            double df0 = s  + t;   df0 = (df0 <= 0.0f) ? (double)1.0f : (1.0f / df0);
            double df1 = sC + t;   df1 = (df1 <= 0.0f) ? (double)1.0f : (1.0f / df1);
            double df2 = sC + tC;  df2 = (df2 <= 0.0f) ? (double)1.0f : (1.0f / df2);
            double df3 = s  + tC;  df3 = (df3 <= 0.0f) ? (double)1.0f : (1.0f / df3);

            //  Make sure the G[i] for pairs of interior points sum to 1 in all cases:
            double[] G = new double[8] { s*df0, (1.0f -  s*df0),
                t*df1, (1.0f -  t*df1),
                sC*df2, (1.0f - sC*df2),
                tC*df3, (1.0f - tC*df3) };

            //  Combined weights for boundary and interior points:
            for (int i = 0; i < 12; ++i)
            {
                point[boundaryGregory[i]] = Bs[boundaryBezSCol[i]] * Bt[boundaryBezTRow[i]];
            }
            for (int i = 0; i < 8; ++i)
            {
                point[interiorGregory[i]] = Bs[interiorBezSCol[i]] * Bt[interiorBezTRow[i]] * G[i];
            }

            //
            //  For derivatives, the basis functions for the interior points are rational and ideally
            //  require appropriate differentiation, i.e. product rule for the combination of B and G
            //  and the quotient rule for the rational G itself.  As initially proposed by Loop et al
            //  though, the approximation using the 16 Bezier points arising from the G(s,t) has
            //  proved adequate (and is what the GPU shaders use) so we continue to use that here.
            //
            //  An implementation of the true derivatives is provided and conditionally compiled for
            //  those that require it, e.g.:
            //
            //    dclyde's note: skipping half of the product rule like this does seem to change the
            //    result a lot in my tests.  This is not a runtime bottleneck for cloth sims anyway
            //    so I'm just using the accurate version.
            //
            if (wDs != null && wDt != null)
            {
                bool find_second_partials = wDs != null && wDst != null && wDtt != null;

                //  Combined weights for boundary points -- simple tensor products:
                for (int i = 0; i < 12; ++i)
                {
                    int iDst = boundaryGregory[i];
                    int tRow = boundaryBezTRow[i];
                    int sCol = boundaryBezSCol[i];

                    wDs[iDst] = Bds[sCol] * Bt[tRow];
                    wDt[iDst] = Bdt[tRow] * Bs[sCol];

                    if (find_second_partials)
                    {
                        wDss[iDst] = Bdss[sCol] * Bt[tRow];
                        wDst[iDst] = Bds[sCol] * Bdt[tRow];
                        wDtt[iDst] = Bs[sCol] * Bdtt[tRow];
                    }
                }

                //  Approximation to the true Gregory derivatives by differentiating the Bezier patch
                //  unique to the given (s,t), i.e. having F = (g^+ * f^+) + (g^- * f^-) as its four
                //  interior points:
                //
                //  Combined weights for interior points -- tensor products with G+ or G-:
                for (int i = 0; i < 8; ++i)
                {
                    int iDst = interiorGregory[i];
                    int tRow = interiorBezTRow[i];
                    int sCol = interiorBezSCol[i];

                    wDs[iDst] = Bds[sCol] * Bt[tRow] * G[i];
                    wDt[iDst] = Bdt[tRow] * Bs[sCol] * G[i];

                    if (find_second_partials)
                    {
                        wDss[iDst] = Bdss[sCol] * Bt[tRow] * G[i];
                        wDst[iDst] = Bds[sCol] * Bdt[tRow] * G[i];
                        wDtt[iDst] = Bs[sCol] * Bdtt[tRow] * G[i];
                    }
                }
            }
            return 20;
        }

        //
        //  Basis support for triangular patches:
        //
        //  Triangular patches may be evaluated in barycentric (trivariate) or
        //  bivariate form, depending on the complexity of their basis functions.
        //  The parametric orientation for a triangle is as follows:
        //
        //            (1,0)
        //              *
        //             . .
        //          t . 2 .
        //           .     .
        //          . 0   1 .
        //   (0,0) *---------* (1,0)
        //              s
        //
        //  With the origin (0,0) -- barycentric (0,0,w = 1) -- oriented at the
        //  corner V0, the corners V0, V1, and V2 correspond to barycentric
        //  coordinates W, U and V.  This is consistent with GPU tessellation
        //  shaders, but not with many publications where the corners correspond
        //  more intuitively to U, V and W.
        //

        //
        //  Simple linear triangle:
        //
        public static int EvalBasisLinearTri(double s, double t,
        double[] wP, double[] wDs = null, double[] wDt = null,
        double[] wDss = null, double[] wDst = null, double[] wDtt = null)
        {

            if (wP != null)
            {
                wP[0] = 1.0f - s - t;
                wP[1] = s;
                wP[2] = t;
            }
            if (wDs != null && wDt != null)
            {
                wDs[0] = -1.0f;
                wDs[1] =  1.0f;
                wDs[2] =  0.0f;

                wDt[0] = -1.0f;
                wDt[1] =  0.0f;
                wDt[2] =  1.0f;

                if (wDss != null && wDst != null && wDtt != null)
                {
                    wDss[0] = wDss[1] = wDss[2] = 0.0f;
                    wDst[0] = wDst[1] = wDst[2] = 0.0f;
                    wDtt[0] = wDtt[1] = wDtt[2] = 0.0f;
                }
            }
            return 3;
        }

        //
        //  Quartic Box spline triangle:
        //
        //  Points for the quartic triangular Box spline (representing regular
        //  patches for Loop subdivision) are as follows:
        //
        //         10-----11
        //         . .   . .
        //        .   . .   .
        //       7-----8-----9
        //      . .   . .   . .
        //     .   . .   . .   .
        //    3-----4-----5-----6
        //     .   . .   . .   .
        //      . .   . .   . /
        //       0-----1-----2
        //
        //  Stam provided the basis functions for these patches in terms of barycentric
        //  coordinates (u,v,w) (see Stam's "Evaluation of Loop Subdivision Surfaces").
        //  Unfortunately, unlike the basis functions for a quartic Bezier triangle,
        //  they are not very compact -- 3 functions involving 9 quartic terms and 3
        //  others involving 15 quartic terms.  (In contrast, the maximum number of
        //  terms in bivariate form is 15.)
        //
        //  Since we also need to differentiate with respect to u and v, we eliminate w
        //  and use the coefficient matrix C multiplied by the set of monomials M
        //  evaluated at (u,v), i.e. the full set of basis functions is:
        //
        //      B(u,v) = C * M(u,v)
        //
        //  where
        //
        //      M(u,v) = { 1, u,v, uu,uv,vv, uuu,uuv,uvv,vvv, uuuu,uuuv,uuvv,uvvv,vvvv }
        //
        //  and the 12 x 15 matrix C is as follows, scaled by a common factor of 1/12:
        //
        //      { 1, -2,-4,    0,  6,  6,   2,  0, -6, -4,  -1, -2, 0,  2,  1 },
        //      { 1,  2,-2,    0, -6,  0,  -4,  0,  6,  2,   2,  4, 0, -2, -1 },
        //      { 0,  0, 0,    0,  0,  0,   2,  0,  0,  0,  -1, -2, 0,  0,  0 },
        //      { 1, -4,-2,    6,  6,  0,  -4, -6,  0,  2,   1,  2, 0, -2, -1 },
        //      { 6,  0, 0,  -12,-12,-12,   8, 12, 12,  8,  -1, -2, 0, -2, -1 },
        //      { 1,  4, 2,    6,  6,  0,  -4, -6,-12, -4,  -1, -2, 0,  4,  2 },
        //      { 0,  0, 0,    0,  0,  0,   0,  0,  0,  0,   1,  2, 0,  0,  0 },
        //      { 1, -2, 2,    0, -6,  0,   2,  6,  0, -4,  -1, -2, 0,  4,  2 },
        //      { 1,  2, 4,    0,  6,  6,  -4,-12, -6, -4,   2,  4, 0, -2, -1 },
        //      { 0,  0, 0,    0,  0,  0,   2,  6,  6,  2,  -1, -2, 0, -2, -1 },
        //      { 0,  0, 0,    0,  0,  0,   0,  0,  0,  2,   0,  0, 0, -2, -1 },
        //      { 0,  0, 0,    0,  0,  0,   0,  0,  0,  0,   0,  0, 0,  2,  1 }
        //
        //  Differentiating the monomials and refactoring yields a unique set of
        //  coefficients for each of the derivatives, which we multiply by M(u,v).
        //
        public static void evalBivariateMonomialsQuartic(double s, double t, double[] M)
        {

            M[0] = 1;

            M[1] = s;
            M[2] = t;

            M[3] = s * s;
            M[4] = s * t;
            M[5] = t * t;

            M[6] = M[3] * s;
            M[7] = M[4] * s;
            M[8] = M[4] * t;
            M[9] = M[5] * t;

            M[10] = M[6] * s;
            M[11] = M[7] * s;
            M[12] = M[3] * M[5];
            M[13] = M[8] * t;
            M[14] = M[9] * t;
        }

        public static void evalBoxSplineTriDerivWeights(double[] stMonomials, int ds, int dt, double[] w)
        {

            double[] M = stMonomials;

            double S = 1;

            int totalOrder = ds + dt;
            if (totalOrder == 0)
            {
                S *= (double) (1.0 / 12.0);

                w[0]  = S * (1 - 2*M[1] - 4*M[2]          + 6*M[4] + 6*M[5] + 2*M[6]          - 6*M[8] - 4*M[9] -   M[10] - 2*M[11] + 2*M[13] +   M[14]);
                w[1]  = S * (1 + 2*M[1] - 2*M[2]          - 6*M[4]          - 4*M[6]          + 6*M[8] + 2*M[9] + 2*M[10] + 4*M[11] - 2*M[13] -   M[14]);
                w[2]  = S * (                                                 2*M[6]                            -   M[10] - 2*M[11]                    );
                w[3]  = S * (1 - 4*M[1] - 2*M[2] + 6*M[3] + 6*M[4]          - 4*M[6] - 6*M[7]          + 2*M[9] +   M[10] + 2*M[11] - 2*M[13] -   M[14]);
                w[4]  = S * (6                   -12*M[3] -12*M[4] -12*M[5] + 8*M[6] +12*M[7] +12*M[8] + 8*M[9] -   M[10] - 2*M[11] - 2*M[13] -   M[14]);
                w[5]  = S * (1 + 4*M[1] + 2*M[2] + 6*M[3] + 6*M[4]          - 4*M[6] - 6*M[7] -12*M[8] - 4*M[9] -   M[10] - 2*M[11] + 4*M[13] + 2*M[14]);
                w[6]  = S * (                                                                                       M[10] + 2*M[11]                    );
                w[7]  = S * (1 - 2*M[1] + 2*M[2]          - 6*M[4]          + 2*M[6] + 6*M[7]          - 4*M[9] -   M[10] - 2*M[11] + 4*M[13] + 2*M[14]);
                w[8]  = S * (1 + 2*M[1] + 4*M[2]          + 6*M[4] + 6*M[5] - 4*M[6] -12*M[7] - 6*M[8] - 4*M[9] + 2*M[10] + 4*M[11] - 2*M[13] -   M[14]);
                w[9]  = S * (                                                 2*M[6] + 6*M[7] + 6*M[8] + 2*M[9] -   M[10] - 2*M[11] - 2*M[13] -   M[14]);
                w[10] = S * (                                                                            2*M[9]                     - 2*M[13] -   M[14]);
                w[11] = S * (                                                                                                         2*M[13] +   M[14]);
            }
            else if (totalOrder == 1)
            {
                S *= (double) (1.0 / 6.0);

                if (ds != 0)
                {
                    w[0]  = S * (-1          + 3*M[2] + 3*M[3]          - 3*M[5] - 2*M[6] - 3*M[7] +   M[9]);
                    w[1]  = S * ( 1          - 3*M[2] - 6*M[3]          + 3*M[5] + 4*M[6] + 6*M[7] -   M[9]);
                    w[2]  = S * (                       3*M[3]                   - 2*M[6] - 3*M[7]         );
                    w[3]  = S * (-2 + 6*M[1] + 3*M[2] - 6*M[3] - 6*M[4]          + 2*M[6] + 3*M[7] -   M[9]);
                    w[4]  = S * (   -12*M[1] - 6*M[2] +12*M[3] +12*M[4] + 6*M[5] - 2*M[6] - 3*M[7] -   M[9]);
                    w[5]  = S * ( 2 + 6*M[1] + 3*M[2] - 6*M[3] - 6*M[4] - 6*M[5] - 2*M[6] - 3*M[7] + 2*M[9]);
                    w[6]  = S * (                                                  2*M[6] + 3*M[7]         );
                    w[7]  = S * (-1          - 3*M[2] + 3*M[3] + 6*M[4]          - 2*M[6] - 3*M[7] + 2*M[9]);
                    w[8]  = S * ( 1          + 3*M[2] - 6*M[3] -12*M[4] - 3*M[5] + 4*M[6] + 6*M[7] -   M[9]);
                    w[9]  = S * (                       3*M[3] + 6*M[4] + 3*M[5] - 2*M[6] - 3*M[7] -   M[9]);
                    w[10] = S * (                                                                  -   M[9]);
                    w[11] = S * (                                                                      M[9]);
                }
                else
                {
                    w[0]  = S * (-2 + 3*M[1] + 6*M[2]          - 6*M[4] - 6*M[5]  -   M[6] + 3*M[8] + 2*M[9]);
                    w[1]  = S * (-1 - 3*M[1]                   + 6*M[4] + 3*M[5]  + 2*M[6] - 3*M[8] - 2*M[9]);
                    w[2]  = S * (                                                 -   M[6]                  );
                    w[3]  = S * (-1 + 3*M[1]          - 3*M[3]          + 3*M[5]  +   M[6] - 3*M[8] - 2*M[9]);
                    w[4]  = S * (   - 6*M[1] -12*M[2] + 6*M[3] +12*M[4] +12*M[5]  -   M[6] - 3*M[8] - 2*M[9]);
                    w[5]  = S * ( 1 + 3*M[1]          - 3*M[3] -12*M[4] - 6*M[5]  -   M[6] + 6*M[8] + 4*M[9]);
                    w[6]  = S * (                                                 +   M[6]                  );
                    w[7]  = S * ( 1 - 3*M[1]          + 3*M[3]          - 6*M[5]  -   M[6] + 6*M[8] + 4*M[9]);
                    w[8]  = S * ( 2 + 3*M[1] + 6*M[2] - 6*M[3] - 6*M[4] - 6*M[5]  + 2*M[6] - 3*M[8] - 2*M[9]);
                    w[9]  = S * (                     + 3*M[3] + 6*M[4] + 3*M[5]  -   M[6] - 3*M[8] - 2*M[9]);
                    w[10] = S * (                                         3*M[5]           - 3*M[8] - 2*M[9]);
                    w[11] = S * (                                                            3*M[8] + 2*M[9]);
                }
            }
            else if (totalOrder == 2)
            {
                if (ds == 2)
                {
                    w[0]  = S * (   +   M[1]          -   M[3] -   M[4]);
                    w[1]  = S * (   - 2*M[1]          + 2*M[3] + 2*M[4]);
                    w[2]  = S * (       M[1]          -   M[3] -   M[4]);
                    w[3]  = S * ( 1 - 2*M[1] -   M[2] +   M[3] +   M[4]);
                    w[4]  = S * (-2 + 4*M[1] + 2*M[2] -   M[3] -   M[4]);
                    w[5]  = S * ( 1 - 2*M[1] -   M[2] -   M[3] -   M[4]);
                    w[6]  = S * (                         M[3] +   M[4]);
                    w[7]  = S * (   +   M[1] +   M[2] -   M[3] -   M[4]);
                    w[8]  = S * (   - 2*M[1] - 2*M[2] + 2*M[3] + 2*M[4]);
                    w[9]  = S * (       M[1] +   M[2] -   M[3] -   M[4]);
                    w[10] =     0;
                    w[11] =     0;
                }
                else if (dt == 2)
                {
                    w[0]  = S * ( 1 -   M[1] - 2*M[2] +   M[4] +   M[5]);
                    w[1]  = S * (   +   M[1] +   M[2] -   M[4] -   M[5]);
                    w[2]  =     0;
                    w[3]  = S * (            +   M[2] -   M[4] -   M[5]);
                    w[4]  = S * (-2 + 2*M[1] + 4*M[2] -   M[4] -   M[5]);
                    w[5]  = S * (   - 2*M[1] - 2*M[2] + 2*M[4] + 2*M[5]);
                    w[6]  =     0;
                    w[7]  = S * (            - 2*M[2] + 2*M[4] + 2*M[5]);
                    w[8]  = S * ( 1 -   M[1] - 2*M[2] -   M[4] -   M[5]);
                    w[9]  = S * (   +   M[1] +   M[2] -   M[4] -   M[5]);
                    w[10] = S * (                M[2] -   M[4] -   M[5]);
                    w[11] = S * (                         M[4] +   M[5]);
                }
                else
                {
                    S *= (double) (1.0 / 2.0);

                    w[0]  = S * ( 1          - 2*M[2] -   M[3] +   M[5]);
                    w[1]  = S * (-1          + 2*M[2] + 2*M[3] -   M[5]);
                    w[2]  = S * (                     -   M[3]         );
                    w[3]  = S * ( 1 - 2*M[1]          +   M[3] -   M[5]);
                    w[4]  = S * (-2 + 4*M[1] + 4*M[2] -   M[3] -   M[5]);
                    w[5]  = S * ( 1 - 2*M[1] - 4*M[2] -   M[3] + 2*M[5]);
                    w[6]  = S * (                     +   M[3]         );
                    w[7]  = S * (-1 + 2*M[1]          -   M[3] + 2*M[5]);
                    w[8]  = S * ( 1 - 4*M[1] - 2*M[2] + 2*M[3] -   M[5]);
                    w[9]  = S * (   + 2*M[1] + 2*M[2] -   M[3] -   M[5]);
                    w[10] = S * (                              -   M[5]);
                    w[11] = S * (                                  M[5]);
                }
            }
            else
            {
                System.Diagnostics.Debug.Assert(totalOrder <= 2);
            }
        }

        public static void adjustBoxSplineTriBoundaryWeights(int boundaryMask, double[] weights)
        {

            if (boundaryMask == 0) return;

            //
            //  Determine boundary edges and vertices from the lower 3 and upper
            //  2 bits of the 5-bit mask:
            //
            int upperBits = (boundaryMask >> 3) & 0x3;
            int lowerBits = boundaryMask & 7;

            int eBits = lowerBits;
            int vBits = 0;

            if (upperBits == 1)
            {
                //  Boundary vertices only:
                vBits = eBits;
                eBits = 0;
            }
            else if (upperBits == 2)
            {
                //  Opposite vertex bit is edge bit rotated one to the right:
                vBits = ((eBits & 1) << 2) | (eBits >> 1);
            }

            bool edge0IsBoundary = (eBits & 1) != 0;
            bool edge1IsBoundary = (eBits & 2) != 0;
            bool edge2IsBoundary = (eBits & 4) != 0;

            //
            //  Adjust weights for the 4 boundary points and 3 interior points
            //  to account for the 3 phantom points adjacent to each
            //  boundary edge:
            //
            if (edge0IsBoundary)
            {
                double w0 = weights[0];
                if (edge2IsBoundary)
                {
                    //  P0 = B1 + (B1 - I1)
                    weights[4] += w0;
                    weights[4] += w0;
                    weights[8] -= w0;
                }
                else
                {
                    //  P0 = B1 + (B0 - I0)
                    weights[4] += w0;
                    weights[3] += w0;
                    weights[7] -= w0;
                }

                //  P1 = B1 + (B2 - I1)
                double w1 = weights[1];
                weights[4] += w1;
                weights[5] += w1;
                weights[8] -= w1;

                double w2 = weights[2];
                if (edge1IsBoundary)
                {
                    //  P2 = B2 + (B2 - I1)
                    weights[5] += w2;
                    weights[5] += w2;
                    weights[8] -= w2;
                }
                else
                {
                    //  P2 = B2 + (B3 - I2)
                    weights[5] += w2;
                    weights[6] += w2;
                    weights[9] -= w2;
                }
                //  Clear weights for the phantom points:
                weights[0] = weights[1] = weights[2] = 0.0f;
            }
            if (edge1IsBoundary)
            {
                double w0 = weights[6];
                if (edge0IsBoundary)
                {
                    //  P0 = B1 + (B1 - I1)
                    weights[5] += w0;
                    weights[5] += w0;
                    weights[4] -= w0;
                }
                else
                {
                    //  P0 = B1 + (B0 - I0)
                    weights[5] += w0;
                    weights[2] += w0;
                    weights[1] -= w0;
                }

                //  P1 = B1 + (B2 - I1)
                double w1 = weights[9];
                weights[5] += w1;
                weights[8] += w1;
                weights[4] -= w1;

                double w2 = weights[11];
                if (edge2IsBoundary)
                {
                    //  P2 = B2 + (B2 - I1)
                    weights[8] += w2;
                    weights[8] += w2;
                    weights[4] -= w2;
                }
                else
                {
                    //  P2 = B2 + (B3 - I2)
                    weights[8]  += w2;
                    weights[10] += w2;
                    weights[7]  -= w2;
                }
                //  Clear weights for the phantom points:
                weights[6] = weights[9] = weights[11] = 0.0f;
            }
            if (edge2IsBoundary)
            {
                double w0 = weights[10];
                if (edge1IsBoundary)
                {
                    //  P0 = B1 + (B1 - I1)
                    weights[8] += w0;
                    weights[8] += w0;
                    weights[5] -= w0;
                }
                else
                {
                    //  P0 = B1 + (B0 - I0)
                    weights[8]  += w0;
                    weights[11] += w0;
                    weights[9]  -= w0;
                }

                //  P1 = B1 + (B2 - I1)
                double w1 = weights[7];
                weights[8] += w1;
                weights[4] += w1;
                weights[5] -= w1;

                double w2 = weights[3];
                if (edge0IsBoundary)
                {
                    //  P2 = B2 + (B2 - I1)
                    weights[4] += w2;
                    weights[4] += w2;
                    weights[5] -= w2;
                }
                else
                {
                    //  P2 = B2 + (B3 - I2)
                    weights[4] += w2;
                    weights[0] += w2;
                    weights[1] -= w2;
                }
                //  Clear weights for the phantom points:
                weights[10] = weights[7] = weights[3] = 0.0f;
            }

            //
            //  Adjust weights for the 3 boundary points and the 2 interior
            //  points to account for the 2 phantom points adjacent to
            //  each boundary vertex:
            //
            if ((vBits & 1) != 0)
            {
                //  P0 = B1 + (B0 - I0)
                double w0 = weights[3];
                weights[4] += w0;
                weights[7] += w0;
                weights[8] -= w0;

                //  P1 = B1 + (B2 - I1)
                double w1 = weights[0];
                weights[4] += w1;
                weights[1] += w1;
                weights[5] -= w1;

                //  Clear weights for the phantom points:
                weights[3] = weights[0] = 0.0f;
            }
            if ((vBits & 2) != 0)
            {
                //  P0 = B1 + (B0 - I0)
                double w0 = weights[2];
                weights[5] += w0;
                weights[1] += w0;
                weights[4] -= w0;

                //  P1 = B1 + (B2 - I1)
                double w1 = weights[6];
                weights[5] += w1;
                weights[9] += w1;
                weights[8] -= w1;

                //  Clear weights for the phantom points:
                weights[2] = weights[6] = 0.0f;
            }
            if ((vBits & 4) != 0)
            {
                //  P0 = B1 + (B0 - I0)
                double w0 = weights[11];
                weights[8] += w0;
                weights[9] += w0;
                weights[5] -= w0;

                //  P1 = B1 + (B2 - I1)
                double w1 = weights[10];
                weights[8] += w1;
                weights[7] += w1;
                weights[4] -= w1;

                //  Clear weights for the phantom points:
                weights[11] = weights[10] = 0.0f;
            }
        }

        public static void boundBasisBoxSplineTri(int boundary,
        double[] wP, double[] wDs, double[] wDt,
        double[] wDss, double[] wDst, double[] wDtt)
        {

            if (wP != null)
            {
                adjustBoxSplineTriBoundaryWeights(boundary, wP);
            }
            if (wDs != null && wDt != null)
            {
                adjustBoxSplineTriBoundaryWeights(boundary, wDs);
                adjustBoxSplineTriBoundaryWeights(boundary, wDt);

                if (wDss != null && wDst != null && wDtt != null)
                {
                    adjustBoxSplineTriBoundaryWeights(boundary, wDss);
                    adjustBoxSplineTriBoundaryWeights(boundary, wDst);
                    adjustBoxSplineTriBoundaryWeights(boundary, wDtt);
                }
            }
        }

        public static int EvalBasisBoxSplineTri(double s, double t,
        double[] wP, double[] wDs = null, double[] wDt = null,
        double[] wDss = null, double[] wDst = null, double[] wDtt = null)
        {

            double[] stMonomials = new double[15];
            evalBivariateMonomialsQuartic(s, t, stMonomials);

            if (wP != null)
            {
                evalBoxSplineTriDerivWeights(stMonomials, 0, 0, wP);
            }
            if (wDs != null && wDt != null)
            {
                evalBoxSplineTriDerivWeights(stMonomials, 1, 0, wDs);
                evalBoxSplineTriDerivWeights(stMonomials, 0, 1, wDt);

                if (wDss != null && wDst != null && wDtt != null)
                {
                    evalBoxSplineTriDerivWeights(stMonomials, 2, 0, wDss);
                    evalBoxSplineTriDerivWeights(stMonomials, 1, 1, wDst);
                    evalBoxSplineTriDerivWeights(stMonomials, 0, 2, wDtt);
                }
            }
            return 12;
        }

        //
        //  Quartic Bezier triangle:
        //
        //  The regular patch for Loop subdivision is a quartic triangular Box spline
        //  with cubic boundaries.  So we need a quartic Bezier patch to represent it
        //  faithfully.
        //
        //  The formulae for the 15 quartic basis functions are:
        //
        //                       4!        i   j   k
        //      B   (u,v,w) =  ------- * (u * v * w )
        //       ijk           i!j!k!
        //
        //  for each i + j + k = 4.   The control points (P) and correspondingly
        //  labeled p<i,j,k> are oriented as follows:
        //
        //                    P14                                   p040
        //                P12     P13                           p031    p130
        //            P9      P10     P11                   p022    p121    p220
        //        P5      P6      P7      P8            p013    p112    p211    p310
        //    P0      P1      P2      P3      P4    p004    p103    p202    p301    p400
        //
        public static void evalBezierTriDerivWeights(double s, double t, int ds, int dt, double[] wB)
        {

            double u  = s;
            double v  = t;
            double w  = 1 - u - v;

            double uu = u * u;
            double vv = v * v;
            double ww = w * w;

            double uv = u * v;
            double vw = v * w;
            double uw = u * w;

            int totalOrder = ds + dt;
            if (totalOrder == 0)
            {
                wB[0]  =      ww * ww;
                wB[1]  =  4 * uw * ww;
                wB[2]  =  6 * uw * uw;
                wB[3]  =  4 * uw * uu;
                wB[4]  =      uu * uu;
                wB[5]  =  4 * vw * ww;
                wB[6]  = 12 * ww * uv;
                wB[7]  = 12 * uu * vw;
                wB[8]  =  4 * uv * uu;
                wB[9]  =  6 * vw * vw;
                wB[10] = 12 * vv * uw;
                wB[11] =  6 * uv * uv;
                wB[12] =  4 * vw * vv;
                wB[13] =  4 * uv * vv;
                wB[14] =      vv * vv;
            }
            else if (totalOrder == 1)
            {
                if (ds == 1)
                {
                    wB[0]  =  -4 * ww * w;
                    wB[1]  =   4 * ww * (w - 3 * u);
                    wB[2]  =  12 * uw * (w - u);
                    wB[3]  =   4 * uu * (3 * w - u);
                    wB[4]  =   4 * uu * u;
                    wB[5]  = -12 * vw * w;
                    wB[6]  =  12 * vw * (w - 2 * u);
                    wB[7]  =  12 * uv * (2 * w - u);
                    wB[8]  =  12 * uv * u;
                    wB[9]  = -12 * vv * w;
                    wB[10] =  12 * vv * (w - u);
                    wB[11] =  12 * vv * u;
                    wB[12] =  -4 * vv * v;
                    wB[13] =   4 * vv * v;
                    wB[14] =   0;
                }
                else
                {
                    wB[0]  =  -4 * ww * w;
                    wB[1]  = -12 * ww * u;
                    wB[2]  = -12 * uu * w;
                    wB[3]  =  -4 * uu * u;
                    wB[4]  =   0;
                    wB[5]  =   4 * ww * (w - 3 * v);
                    wB[6]  =  12 * uw * (w - 2 * v);
                    wB[7]  =  12 * uu * (w - v);
                    wB[8]  =   4 * uu * u;
                    wB[9]  =  12 * vw * (w - v);
                    wB[10] =  12 * uv * (2 * w - v);
                    wB[11] =  12 * uv * u;;
                    wB[12] =   4 * vv * (3 * w - v);
                    wB[13] =  12 * vv * u;
                    wB[14] =   4 * vv * v;
                }
            }
            else if (totalOrder == 2)
            {
                if (ds == 2)
                {
                    wB[0]  =  12 * ww;
                    wB[1]  =  24 * (uw - ww);
                    wB[2]  =  12 * (uu - 4 * uw + ww);
                    wB[3]  =  24 * (uw - uu);
                    wB[4]  =  12 * uu;
                    wB[5]  =  24 * vw;
                    wB[6]  =  24 * (uv - 2 * vw);
                    wB[7]  =  24 * (vw - 2 * uv);
                    wB[8]  =  24 * uv;
                    wB[9]  =  12 * vv;
                    wB[10] = -24 * vv;
                    wB[11] =  12 * vv;
                    wB[12] =   0;
                    wB[13] =   0;
                    wB[14] =   0;
                }
                else if (dt == 2)
                {
                    wB[0]  =  12 * ww;
                    wB[1]  =  24 * uw;
                    wB[2]  =  12 * uu;
                    wB[3]  =   0;
                    wB[4]  =   0;
                    wB[5]  =  24 * (vw - ww);
                    wB[6]  =  24 * (uv - 2 * uw);
                    wB[7]  = -24 * uu;
                    wB[8]  =   0;
                    wB[9]  =  12 * (vv - 4 * vw + ww);
                    wB[10] =  24 * (uw - 2 * uv);
                    wB[11] =  12 * uu;
                    wB[12] =  24 * (vw - vv);
                    wB[13] =  24 * uv;
                    wB[14] =  12 * vv;
                }
                else
                {
                    wB[0]  =  12 * ww;
                    wB[3]  = -12 * uu;
                    wB[13] =  12 * vv;
                    wB[11] =  24 * uv;
                    wB[1]  =  24 * uw - wB[0];
                    wB[2]  = -24 * uw - wB[3];
                    wB[5]  =  24 * vw - wB[0];
                    wB[6]  = -24 * vw + wB[11] - wB[1];
                    wB[8]  = - wB[3];
                    wB[7]  = -(wB[11] + wB[2]);
                    wB[9]  =   wB[13] - wB[5] - wB[0];
                    wB[10] = -(wB[9] + wB[11]);
                    wB[12] = - wB[13];
                    wB[4]  =   0;
                    wB[14] =   0;
                }
            }
            else
            {
                System.Diagnostics.Debug.Assert(totalOrder <= 2);
            }
        }

        public static int EvalBasisBezierTri(double s, double t,
        double[] wP, double[] wDs = null, double[] wDt = null,
        double[] wDss = null, double[] wDst = null, double[] wDtt = null)
        {

            if (wP != null)
            {
                evalBezierTriDerivWeights(s, t, 0, 0, wP);
            }
            if (wDs != null && wDt != null)
            {
                evalBezierTriDerivWeights(s, t, 1, 0, wDs);
                evalBezierTriDerivWeights(s, t, 0, 1, wDt);

                if (wDss != null && wDst != null && wDtt != null)
                {
                    evalBezierTriDerivWeights(s, t, 2, 0, wDss);
                    evalBezierTriDerivWeights(s, t, 1, 1, wDst);
                    evalBezierTriDerivWeights(s, t, 0, 2, wDtt);
                }
            }
            return 15;
        }

        //
        //  Quartic Gregory triangle:
        //
        //  The 18-point quaritic Gregory patch is an extension of the 15-point
        //  quartic Bezier triangle with the 3 interior points of the Bezier patch
        //  replaced with pairs of points (face points -- fi+ and fi-) that are
        //  rationally combined.
        //
        //  The point ordering of Gregory patches deviates considerably from the
        //  BSpline and Bezier patches by grouping the 5 points at each corner and
        //  ordering the groups by corner index.  This is consistent with the cubic
        //  Gregory quad patch.
        //
        //  The 3 additional quartic boundary points are currently appended to these
        //  3 groups of 5 control points.  In contrast to the 5 points associated
        //  with each corner, these 3 points are more associated with the edge
        //  between the corner vertices and are equally weighted between the two.
        //
        //
        //  Expanding a set of 15 Bezier basis functions for the 6 (3 pairs) of
        //  rational weights for the 18 Gregory basis functions:
        //
        public static void convertBezierWeightsToGregory(double[] wB, double[] rG, double[] wG)
        {

            wG[0]  = wB[0];
            wG[1]  = wB[1];
            wG[2]  = wB[5];
            wG[3]  = wB[6] * rG[0];
            wG[4]  = wB[6] * rG[1];

            wG[5]  = wB[4];
            wG[6]  = wB[8];
            wG[7]  = wB[3];
            wG[8]  = wB[7] * rG[2];
            wG[9]  = wB[7] * rG[3];

            wG[10] = wB[14];
            wG[11] = wB[12];
            wG[12] = wB[13];
            wG[13] = wB[10] * rG[4];
            wG[14] = wB[10] * rG[5];

            wG[15] = wB[2];
            wG[16] = wB[11];
            wG[17] = wB[9];
        }

        public static int EvalBasisGregoryTri(double s, double t,
        double[] wP, double[] wDs = null, double[] wDt = null,
        double[] wDss = null, double[] wDst = null, double[] wDtt = null)
        {

            //
            //  Bezier basis functions are denoted with B while the rational multipliers for the
            //  interior points will be denoted G -- so we have B(s,t) and G(s,t) (though we
            //  switch to barycentric (u,v,w) briefly to compute G)
            //
            double[] BP = new double[15];
            double[] BDs = new double[15];
            double[] BDt = new double[15];
            double[] BDss = new double[15];
            double[] BDst = new double[15];
            double[] BDtt = new double[15];

            double[] G = new double[6] { 1.0f, 0.0f, 1.0f, 0.0f, 1.0f, 0.0f };
            double u = s;
            double v = t;
            double w = 1 - u - v;

            if ((u + v) > 0)
            {
                G[0]  = u / (u + v);
                G[1]  = v / (u + v);
            }
            if ((v + w) > 0)
            {
                G[2] = v / (v + w);
                G[3] = w / (v + w);
            }
            if ((w + u) > 0)
            {
                G[4] = w / (w + u);
                G[5] = u / (w + u);
            }

            //
            //  Compute Bezier basis functions and convert, adjusting interior points:
            //
            if (wP != null)
            {
                evalBezierTriDerivWeights(s, t, 0, 0, BP);
                convertBezierWeightsToGregory(BP, G, wP);
            }
            if (wDs != null && wDt != null)
            {
                //  TBD -- ifdef OPENSUBDIV_GREGORY_EVAL_TRUE_DERIVATIVES

                evalBezierTriDerivWeights(s, t, 1, 0, BDs);
                evalBezierTriDerivWeights(s, t, 0, 1, BDt);

                convertBezierWeightsToGregory(BDs, G, wDs);
                convertBezierWeightsToGregory(BDt, G, wDt);

                if (wDss != null && wDst != null && wDtt != null)
                {
                    evalBezierTriDerivWeights(s, t, 2, 0, BDss);
                    evalBezierTriDerivWeights(s, t, 1, 1, BDst);
                    evalBezierTriDerivWeights(s, t, 0, 2, BDtt);

                    convertBezierWeightsToGregory(BDss, G, wDss);
                    convertBezierWeightsToGregory(BDst, G, wDst);
                    convertBezierWeightsToGregory(BDtt, G, wDtt);
                }
            }
            return 18;
        }

        //
        //  Higher level basis evaluation functions that deal with parameterization and
        //  boundary issues (reflected in PatchParam) for all patch types:
        //
        public static int EvaluatePatchBasisNormalized(int patchType, PatchParam param, double s, double t,
        double[] wP, double[] wDs = null, double[] wDt = null,
        double[] wDss = null, double[] wDst = null, double[] wDtt = null)
        {

            int boundaryMask = param.GetBoundary();

            int nPoints = 0;
            if (patchType == (int)PatchDescriptor.Type.REGULAR)
            {
                nPoints = EvalBasisBSpline(s, t, wP, wDs, wDt, wDss, wDst, wDtt);
                if (boundaryMask != 0)
                {
                    boundBasisBSpline(boundaryMask, wP, wDs, wDt, wDss, wDst, wDtt);
                }
            }
            else if (patchType == (int)PatchDescriptor.Type.LOOP)
            {
                nPoints = EvalBasisBoxSplineTri(s, t, wP, wDs, wDt, wDss, wDst, wDtt);
                if (boundaryMask != 0)
                {
                    boundBasisBoxSplineTri(boundaryMask, wP, wDs, wDt, wDss, wDst, wDtt);
                }
            }
            else if (patchType == (int)PatchDescriptor.Type.GREGORY_BASIS)
            {
                nPoints = EvalBasisGregory(s, t, wP, wDs, wDt, wDss, wDst, wDtt);
            }
            else if (patchType == (int)PatchDescriptor.Type.GREGORY_TRIANGLE)
            {
                nPoints = EvalBasisGregoryTri(s, t, wP, wDs, wDt, wDss, wDst, wDtt);
            }
            else if (patchType == (int)PatchDescriptor.Type.QUADS)
            {
                nPoints = EvalBasisLinear(s, t, wP, wDs, wDt, wDss, wDst, wDtt);
            }
            else if (patchType == (int)PatchDescriptor.Type.TRIANGLES)
            {
                nPoints = EvalBasisLinearTri(s, t, wP, wDs, wDt, wDss, wDst, wDtt);
            }
            else
            {
                System.Diagnostics.Debug.Assert(false);
            }
            return nPoints;
        }

        public static int EvaluatePatchBasis(int patchType, PatchParam param, double s, double t,
        double[] wP, double[] wDs = null, double[] wDt = null,
        double[] wDss = null, double[] wDst = null, double[] wDtt = null)
        {

            double derivSign = 1.0f;

            if ((patchType == (int)PatchDescriptor.Type.LOOP) ||
            (patchType == (int)PatchDescriptor.Type.GREGORY_TRIANGLE) ||
            (patchType == (int)PatchDescriptor.Type.TRIANGLES))
            {
                param.NormalizeTriangle(ref s, ref t);
                if (param.IsTriangleRotated())
                {
                    derivSign = -1.0f;
                }
            }
            else
            {
                param.Normalize(ref s, ref t);
            }

            int nPoints = EvaluatePatchBasisNormalized(
            patchType, param, s, t, wP, wDs, wDt, wDss, wDst, wDtt);

            if (wDs != null && wDt != null)
            {
                double d1Scale = derivSign * (double)(1 << param.GetDepth());

                for (int i = 0; i < nPoints; ++i)
                {
                    wDs[i] *= d1Scale;
                    wDt[i] *= d1Scale;
                }

                if (wDss != null && wDst != null && wDtt != null)
                {
                    double d2Scale = derivSign * d1Scale * d1Scale;

                    for (int i = 0; i < nPoints; ++i)
                    {
                        wDss[i] *= d2Scale;
                        wDst[i] *= d2Scale;
                        wDtt[i] *= d2Scale;
                    }
                }
            }
            return nPoints;
        }

        //
        //  Explicit float and double instantiations:
        //
    }
}
