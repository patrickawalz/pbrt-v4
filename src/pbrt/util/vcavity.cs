// (not in pbrt-v4) util/vcavity.cs
//
// V-cavity ("kaleidoscopic") microfacet geometry, after
//   Joo Ho Lee, Adrian Jarabo, Daniel S. Jeon, Diego Gutierrez, Min H. Kim,
//   "Practical Multiple Scattering for Rough Surfaces" (SIGGRAPH Asia 2018).
//
// The exact 2D cross-section solver of a V-shaped groove (symmetric or not): given the elevation of the incident ray's
// projection onto the groove cross-section and the tilts of the two facet normals, it returns every distinct outcome
// (first-hit facet, number of reflections n, exit elevation, probability). The probabilities are exact areas of the incident
// beam (computed by unfolding the groove into a fan of mirror images), not a Monte Carlo estimate.
//
// Conventions (cross-section plane spanned by t and the macro normal n = z):
//   an in-plane direction is written (sin theta, cos theta) in (t, n) coordinates,
//   facet normal tilt is measured from n toward +t.
//   Left wall: normal tilt tl in (0, pi/2) (the wall sits on the -t side of the apex),
//   right wall: normal tilt tr in (-pi/2, 0). Aperture angle thetaV = pi - tl + tr.

using System;

namespace pbrt
{
    // VCavityOutcome Definition
    public struct VCavityOutcome
    {
        // 0: first hit is the left wall, 1: first hit is the right wall
        public int first;
        // number of reflections
        public int n;
        // probability (fraction of the incident beam)
        public double prob;
        // in-plane exit elevation, in (-pi/2, pi/2)
        public double thetaO;
    }

    public static unsafe class VCavity
    {
        public const double kPi = 3.14159265358979323846;
        public const int kMaxOutcomes = 2 * 66;

        public static double WrapPi(double a)
        {
            a = Math.IEEERemainder(a, 2 * kPi);
            if (a >= kPi)
            {
                a -= 2 * kPi;
            }
            if (a < -kPi)
            {
                a += 2 * kPi;
            }
            return a;
        }

        // Outcomes of a beam that hits the LEFT wall first. Wedge described by wall line angles psiL (left wall direction from
        // the apex, in (pi/2.., pi)) and psiR (right wall direction). Appends to output[], returns number appended.
        public static int TraceLeftFirst(double thetaI, double tl, double tr, int maxN, VCavityOutcome* output)
        {
            double psiL = kPi - tl, psiR = -tr;
            double thV = psiL - psiR;
            if (!(thV > 1e-9) || !(thV < kPi))
            {
                return 0;
            }
            double sL = Math.Sin(psiL), sR = Math.Sin(psiR);
            // wall lengths (aperture at z = 1)
            double rL = 1 / sL, rR = 1 / sR;
            double xL = Math.Cos(psiL) * rL, xR = Math.Cos(psiR) * rR;
            double cosI = Math.Cos(thetaI), sinI = Math.Sin(thetaI);
            if (!(cosI > 1e-12))
            {
                return 0;
            }
            // offset of a ray through (x, 1): c = -x cos(thetaI) + sin(thetaI); decreasing in x
            double cL = -xL * cosI + sinI, cR = -xR * cosI + sinI;
            double total = cL - cR;
            double lo = Math.Max(0.0, cR), hi = cL;
            if (!(hi > lo))
            {
                return 0;
            }
            int cnt = 0;
            double psi = -kPi / 2 - thetaI;
            // running minimum of tip offsets; meas(n >= 1) = hi - lo
            double m = hi, measPrev = hi - lo;
            for (int j = 1; j <= maxN; ++j)
            {
                // meas(n >= j+1) = max(0, min(m_{j+1}, hi) - lo) with m_{j+1} = min(m_j, c_{j+1}); wall j+1 of the unfolded fan
                // has the polar angle psiL + j thV, and its radius alternates rL, rR
                double Psi1 = psiL + j * thV;
                double r1 = (((j + 1) & 1) != 0) ? rL : rR;
                double cj1 = -r1 * Math.Cos(Psi1 + thetaI);
                double mNext = Math.Min(m, cj1);
                double measNext = Math.Max(0.0, Math.Min(mNext, hi) - lo);
                // reflect direction across wall j (line angle psiL for odd j, psiR for even j)
                double lam = ((j & 1) != 0) ? psiL : psiR;
                psi = 2 * lam - psi;
                double pj = (measPrev - measNext) / total;
                if (pj > 0)
                {
                    output[cnt].first = 0;
                    output[cnt].n = j;
                    output[cnt].prob = pj;
                    output[cnt].thetaO = WrapPi(kPi / 2 - psi);
                    ++cnt;
                }
                m = mNext;
                measPrev = measNext;
                if (!(measPrev > 0))
                {
                    break;
                }
            }
            return cnt;
        }

        // All outcomes of the V groove for a ray with in-plane elevation thetaI. Left-first outcomes come first, then
        // right-first. Probabilities sum to 1 (minus mass beyond maxN reflections).
        public static int Trace(double thetaI, double tl, double tr, int maxN, VCavityOutcome* output)
        {
            int n = TraceLeftFirst(thetaI, tl, tr, maxN, output);
            // Right-first by mirror symmetry: (thetaI, tl, tr) -> (-thetaI, -tr, -tl), thetaO -> -thetaO
            int m = TraceLeftFirst(-thetaI, -tr, -tl, maxN, output + n);
            for (int i = 0; i < m; ++i)
            {
                output[n + i].first = 1;
                output[n + i].thetaO = -output[n + i].thetaO;
            }
            return n + m;
        }

        // In-plane cosines of the incidence angle at each bounce of a path (unit in-plane vector),
        // cosines[j-1] = |sin(psi_{j-1} - lambda_j)|. Returns n.
        public static int PathCosines(double thetaI, double tl, double tr, int first, int n, double* cosines)
        {
            double psiL = kPi - tl, psiR = -tr;
            double psi = -kPi / 2 - thetaI;
            for (int j = 1; j <= n; ++j)
            {
                bool leftWall = (first == 0) ? ((j & 1) != 0) : ((j & 1) == 0);
                double lam = leftWall ? psiL : psiR;
                cosines[j - 1] = Math.Abs(Math.Sin(psi - lam));
                psi = 2 * lam - psi;
            }
            return n;
        }

        // Fraction of the incident beam that strikes a j-th wall (j = 1..maxN) when the first hit is the LEFT wall (reach[j-1]);
        // a ray that reaches the j-th wall has reflected j-1 times. Used for the transmissive extension: transmission at wall j
        // has probability reach_j * prod R_k (k<j) * T_j.
        public static void ReachLeftFirst(double thetaI, double tl, double tr, int maxN, double* reach)
        {
            for (int j = 0; j < maxN; ++j)
            {
                reach[j] = 0;
            }
            double psiL = kPi - tl, psiR = -tr;
            double thV = psiL - psiR;
            if (!(thV > 1e-9) || !(thV < kPi))
            {
                return;
            }
            double sL = Math.Sin(psiL), sR = Math.Sin(psiR);
            double rL = 1 / sL, rR = 1 / sR;
            double xL = Math.Cos(psiL) * rL, xR = Math.Cos(psiR) * rR;
            double cosI = Math.Cos(thetaI), sinI = Math.Sin(thetaI);
            if (!(cosI > 1e-12))
            {
                return;
            }
            double cL = -xL * cosI + sinI, cR = -xR * cosI + sinI;
            double total = cL - cR;
            double lo = Math.Max(0.0, cR), hi = cL;
            if (!(hi > lo))
            {
                return;
            }
            double m = hi, measPrev = hi - lo;
            for (int j = 1; j <= maxN; ++j)
            {
                reach[j - 1] = measPrev / total;
                double Psi1 = psiL + j * thV;
                double r1 = (((j + 1) & 1) != 0) ? rL : rR;
                double cj1 = -r1 * Math.Cos(Psi1 + thetaI);
                double mNext = Math.Min(m, cj1);
                double measNext = Math.Max(0.0, Math.Min(mNext, hi) - lo);
                m = mNext;
                measPrev = measNext;
                if (!(measPrev > 0))
                {
                    break;
                }
            }
        }

        // reachL: left-first walls, reachR: right-first walls (mirror symmetry as in Trace)
        public static void ReachAll(double thetaI, double tl, double tr, int maxN, double* reachL, double* reachR)
        {
            ReachLeftFirst(thetaI, tl, tr, maxN, reachL);
            ReachLeftFirst(-thetaI, -tr, -tl, maxN, reachR);
        }

        private static double Cross(double ax, double ay, double bx, double by)
        {
            return ax * by - ay * bx;
        }

        // Parallel beam of rays entering the wedge through its right wall (edge E0, polar angle alpha, length 1/sin(alpha)) with
        // in-plane travel direction (dt, dz), uniformly distributed along the wall. The wedge is unfolded about its apex (copy j
        // = j-fold reflection, edges E_j at polar angle alpha + j W with W = pi - 2 alpha, chords between the edge tips), where
        // rays are straight lines.
        // exitM[m]  : fraction of rays that leave through the aperture after exactly m wall hits (m <= maxN)
        // reachM[j] : fraction of rays that hit a j-th wall (1 <= j <= maxN)
        // The topology is piecewise constant in the entry position; the breakpoints are the positions whose ray passes through
        // an edge tip, found analytically. (Entry through the left wall: mirror dt.) Both arrays have maxN + 2 elements.
        public static void WedgeBeam(double alpha, double dt, double dz, int maxN, double* exitM, double* reachM)
        {
            for (int m = 0; m <= maxN + 1; ++m)
            {
                exitM[m] = reachM[m] = 0;
            }
            double W = kPi - 2 * alpha, Lw = 1 / Math.Sin(alpha);
            double e0x = Math.Cos(alpha), e0y = Math.Sin(alpha);
            double den = Cross(dt, dz, e0x, e0y);
            double* bp = stackalloc double[140];
            int nb = 0;
            bp[nb++] = 0;
            if (Math.Abs(den) > 1e-14)
            {
                for (int j = 1; j <= maxN + 1 && nb < 136; ++j)
                {
                    double ang = alpha + j * W;
                    double r = Cross(dt, dz, Lw * Math.Cos(ang), Lw * Math.Sin(ang)) / den;
                    if (r > 0 && r < Lw)
                    {
                        bp[nb++] = r;
                    }
                }
            }
            bp[nb++] = Lw;
            // sort
            for (int a = 1; a < nb; ++a)
            {
                double v = bp[a];
                int b = a - 1;
                while (b >= 0 && bp[b] > v)
                {
                    bp[b + 1] = bp[b];
                    --b;
                }
                bp[b + 1] = v;
            }
            for (int b = 0; b + 1 < nb; ++b)
            {
                double lo = bp[b], hi = bp[b + 1];
                if (!(hi - lo > 1e-15))
                {
                    continue;
                }
                double w = (hi - lo) / Lw, rm = 0.5 * (lo + hi);
                double px = rm * e0x, py = rm * e0y;
                int hits = 0;
                bool done = false;
                for (int j = 0; j <= maxN && !done; ++j)
                {
                    double a0 = alpha + j * W, a1 = alpha + (j + 1) * W;
                    double t0x = Lw * Math.Cos(a0), t0y = Lw * Math.Sin(a0);
                    double t1x = Lw * Math.Cos(a1), t1y = Lw * Math.Sin(a1);
                    // chord: P + l d = T0 + s (T1 - T0)
                    double cx = t1x - t0x, cy = t1y - t0y;
                    double dd = Cross(dt, dz, cx, cy);
                    double lc = 1e300, le = 1e300;
                    if (Math.Abs(dd) > 1e-18)
                    {
                        double l = Cross(t0x - px, t0y - py, cx, cy) / dd;
                        double s = Cross(t0x - px, t0y - py, dt, dz) / dd;
                        if (l > 0 && s >= -1e-12 && s <= 1 + 1e-12)
                        {
                            lc = l;
                        }
                    }
                    // edge E_{j+1}: line through O along u
                    double ux = Math.Cos(a1), uy = Math.Sin(a1);
                    double de = Cross(dt, dz, ux, uy);
                    if (Math.Abs(de) > 1e-18)
                    {
                        double l = -Cross(px, py, ux, uy) / de;
                        double rho = (px + l * dt) * ux + (py + l * dz) * uy;
                        if (l > 0 && rho > 0 && rho <= Lw * (1 + 1e-12))
                        {
                            le = l;
                        }
                    }
                    if (lc < le)
                    {
                        exitM[hits] += w;
                        done = true;
                    }
                    else if (le < 1e299)
                    {
                        ++hits;
                        if (hits <= maxN)
                        {
                            reachM[hits] += w;
                        }
                        else
                        {
                            done = true;
                        }
                    }
                    else
                    {
                        done = true;
                    }
                }
            }
        }
    }
}
