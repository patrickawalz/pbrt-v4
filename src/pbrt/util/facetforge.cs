// (not in pbrt-v4) facetforge.cs
//
// Facet Forge: "Student-T and Beyond" (d'Eon, SIGGRAPH 2023 Talks). A direct port of util/ffnum.h and util/facetforge.h of the
// C++ fork, which port the algorithms of NVIDIA's facet-forge reference code (Apache-2.0, https://github.com/NVlabs/facet-forge)
// to pbrt-v4 types:
//   * Microsurface operator: (facet BSDF, NDF) -> BSDF, by an exact Monte Carlo random walk in a half-space (stochastic sample /
//     NEE evaluation), recursively applicable (biscale).
//   * NDFs: Beckmann, GGX, Student-T (heightfield, anisotropic), blends of up to 4 NDFs, and full-sphere NDFs via null
//     scattering (vMF, null Student-T).
//
// Differences from the reference code (all deliberate, and those of the fork):
//   * Student-T cross section sigma(w) is the EXACT closed form (Student-t marginal slope CDF), the reference uses a fitted
//     approximation (auxF). The reference fit is kept for comparison (StudentTSigmaReferenceFit).
//   * Student-T visible-normal sampling defaults to EXACT slope-space CDF inversion (visible slope marginal, then exact Student-t
//     conditional). The paper's Beckmann-superposition sampler (fitted mixture probabilities) is available (sampler "paper") and
//     is approximate.
//   * Null NDFs sample free paths with the exact sigma (precomputed from the Dirac-ring Green's function) and sample visible
//     normals by disk-sampling + D/Dbar rejection; this is the thinning of the paper's constant-majorant process, so it is
//     distributionally identical. The literal majorant-collision walk of the reference is also available ("literalnull").
//   * Spectral facets (conductor Fresnel, reflectances) via SampledSpectrum; geometry in double.
//
// BSDF value convention (as the reference): eval() = BSDF * |cos(wo)| in "energy" form; pbrt's Radiance-mode 1/eta^2 scale is
// applied once to the whole walk (net crossing).
//
// The C++ namespace pbrt::ff is the static class ff. The structures that the C++ copies by value are classes with Clone().

using System;
using System.Collections.Generic;
using System.Numerics;
using Attenuation = pbrt.PolarisedLightSystem.Attenuation;

namespace pbrt
{
    public static unsafe partial class ff
    {
        public const double ffPi = 3.14159265358979323846;
        public const int kMaxComps = 4;

        // ------------------------------------------------------------------------------------------
        // small double-precision vector
        // ------------------------------------------------------------------------------------------
        public struct V3
        {
            public double x, y, z;

            public V3(double x, double y, double z)
            {
                this.x = x;
                this.y = y;
                this.z = z;
            }

            public static V3 operator +(V3 a, V3 b) { return new V3(a.x + b.x, a.y + b.y, a.z + b.z); }
            public static V3 operator -(V3 a, V3 b) { return new V3(a.x - b.x, a.y - b.y, a.z - b.z); }
            public static V3 operator -(V3 a) { return new V3(-a.x, -a.y, -a.z); }
            public static V3 operator *(double s, V3 a) { return new V3(s * a.x, s * a.y, s * a.z); }
        }

        public static double Dot(V3 a, V3 b) { return a.x * b.x + a.y * b.y + a.z * b.z; }

        public static V3 Normalize(V3 a)
        {
            double l = Math.Sqrt(Dot(a, a));
            return l > 0 ? (1.0 / l) * a : new V3(0, 0, 1);
        }

        public static V3 Reflect(V3 wi, V3 n) { return 2 * Dot(wi, n) * n - wi; }

        // Duff et al. orthonormal basis
        public static void Basis(V3 n, out V3 a, out V3 b)
        {
            double s = Math.CopySign(1.0, n.z);
            double aa = -1.0 / (s + n.z);
            double bb = n.x * n.y * aa;
            a = new V3(1 + s * n.x * n.x * aa, s * bb, -s * n.x);
            b = new V3(bb, s + n.y * n.y * aa, -n.y);
        }

        // Facet frame for hierarchical (biscale) levels: tangent a = projection of the parent frame's x axis onto the facet
        // plane, b = n x a. For n = z this is exactly (x, y, z). Needed so that an anisotropic inner NDF keeps its orientation
        // relative to the macro tangent on every outer facet (Duff's basis rotates arbitrarily about n). Degenerate only for n
        // parallel to +-x (fallback).
        public static void TangentBasis(V3 n, out V3 a, out V3 b)
        {
            V3 t = new V3(1.0 - n.x * n.x, -n.x * n.y, -n.x * n.z);
            double l2 = Dot(t, t);
            if (!(l2 > 1e-12))
            {
                Basis(n, out a, out b);
                return;
            }
            a = (1.0 / Math.Sqrt(l2)) * t;
            b = new V3(n.y * a.z - n.z * a.y, n.z * a.x - n.x * a.z, n.x * a.y - n.y * a.x);
        }

        // ------------------------------------------------------------------------------------------
        // random numbers
        // ------------------------------------------------------------------------------------------
        public sealed class Rnd
        {
            private RNG r;

            public Rnd(RNG r)
            {
                this.r = r;
            }

            public double U() { return r.Uniform<double>(); }

            public double Normal()
            {
                double u1 = U(), u2 = U();
                return Math.Sqrt(-2 * Log1p(-u1)) * Math.Cos(2 * ffPi * u2);
            }

            // Marsaglia-Tsang gamma variate, shape a > 0, scale 1
            public double Gamma(double a)
            {
                if (a < 1)
                {
                    double u = U();
                    return Gamma(a + 1) * Math.Pow(1 - u, 1.0 / a);
                }
                double d = a - 1.0 / 3, c = 1.0 / Math.Sqrt(9 * d);
                for (;;)
                {
                    double x = Normal(), v = 1 + c * x;
                    if (v <= 0)
                    {
                        continue;
                    }
                    v = v * v * v;
                    double u = U();
                    if (Log1p(-u) < 0.5 * x * x + d - d * v + d * Math.Log(v))
                    {
                        return d * v;
                    }
                }
            }
        }

        // ------------------------------------------------------------------------------------------
        // special functions
        // ------------------------------------------------------------------------------------------
        // (std::log1p and std::expm1)
        public static double Log1p(double x)
        {
            if (Math.Abs(x) > 1e-4)
            {
                return Math.Log(1.0 + x);
            }
            return (-0.5 * x + 1.0) * x;
        }

        public static double Expm1(double x)
        {
            if (Math.Abs(x) < 1e-5)
            {
                return x + 0.5 * x * x;
            }
            return Math.Exp(x) - 1.0;
        }

        // (std::tgamma, by the logarithm and the reflection formula)
        public static double TGamma(double x)
        {
            if (x > 0)
            {
                return Math.Exp(pbrt.LogGamma(x));
            }
            return ffPi / (Math.Sin(ffPi * x) * Math.Exp(pbrt.LogGamma(1 - x)));
        }

        public static double BetaCF(double a, double b, double x)
        {
            const int MAXIT = 600;
            const double EPS = 1e-15, FPMIN = 1e-300;
            double qab = a + b, qap = a + 1, qam = a - 1, c = 1, d = 1 - qab * x / qap;
            if (Math.Abs(d) < FPMIN)
            {
                d = FPMIN;
            }
            d = 1 / d;
            double h = d;
            for (int m = 1; m <= MAXIT; ++m)
            {
                int m2 = 2 * m;
                double aa = m * (b - m) * x / ((qam + m2) * (a + m2));
                d = 1 + aa * d;
                if (Math.Abs(d) < FPMIN)
                {
                    d = FPMIN;
                }
                c = 1 + aa / c;
                if (Math.Abs(c) < FPMIN)
                {
                    c = FPMIN;
                }
                d = 1 / d;
                h *= d * c;
                aa = -(a + m) * (qab + m) * x / ((a + m2) * (qap + m2));
                d = 1 + aa * d;
                if (Math.Abs(d) < FPMIN)
                {
                    d = FPMIN;
                }
                c = 1 + aa / c;
                if (Math.Abs(c) < FPMIN)
                {
                    c = FPMIN;
                }
                d = 1 / d;
                double del = d * c;
                h *= del;
                if (Math.Abs(del - 1) < EPS)
                {
                    break;
                }
            }
            return h;
        }

        // regularized incomplete beta I_x(a,b)
        public static double IncBeta(double a, double b, double x)
        {
            if (x <= 0)
            {
                return 0;
            }
            if (x >= 1)
            {
                return 1;
            }
            double bt = Math.Exp(pbrt.LogGamma(a + b) - pbrt.LogGamma(a) - pbrt.LogGamma(b) + a * Math.Log(x) + b * Log1p(-x));
            if (x < (a + 1) / (a + b + 2))
            {
                return bt * BetaCF(a, b, x) / a;
            }
            return 1 - bt * BetaCF(b, a, 1 - x) / b;
        }

        // ------------------------------------------------------------------------------------------
        // Slope-space marginals (unit roughness). A heightfield NDF with slope density P22(p,q) has visible-slope density
        // (u - p s) f(p) g(q|p) with f the marginal along the plane of incidence. F = CDF of f, M(p) = int_{-inf}^p s f(s) ds.
        // Then, with x = u/s (u = cos, s = sin of the (stretched) incident direction):
        //     sigma = u F(x) - s M(x)       and the visible slope CDF is [u F(p) - s M(p)] / sigma, p < x.
        // ------------------------------------------------------------------------------------------
        public enum Kind
        {
            Beckmann,
            GGX,
            StudentT,
            VMF,
            NullStudentT
        }

        public struct SlopeDist
        {
            public bool beckmann;
            public double gamma;  // Student-T shape (GGX = 2)
            public double logC;

            public void Init(Kind k, double g)
            {
                beckmann = (k == Kind.Beckmann);
                gamma = (k == Kind.GGX) ? 2.0 : g;
                logC = 0;
                if (!beckmann)
                {
                    logC = gamma * Math.Log(gamma - 1) + pbrt.LogGamma(gamma - 0.5) - pbrt.LogGamma(gamma) - 0.5 * Math.Log(ffPi);
                }
            }

            public double f(double p)
            {
                if (beckmann)
                {
                    return Math.Exp(-p * p) / Math.Sqrt(ffPi);
                }
                return Math.Exp(logC + (0.5 - gamma) * Math.Log(gamma - 1 + p * p));
            }

            public double F(double p)
            {
                if (beckmann)
                {
                    return 0.5 * pbrt.Erfc(-p);
                }
                double nu = 2 * gamma - 2;
                double tail = 0.5 * IncBeta(nu / 2, 0.5, (gamma - 1) / (gamma - 1 + p * p));
                return p < 0 ? tail : 1 - tail;
            }

            public double M(double p)
            {
                if (beckmann)
                {
                    return -Math.Exp(-p * p) / (2 * Math.Sqrt(ffPi));
                }
                return -Math.Exp(logC + (1.5 - gamma) * Math.Log(gamma - 1 + p * p)) / (2 * gamma - 3);
            }

            // sigma for (u, s) with s > 0; u may be negative
            public double Sigma(double u, double s)
            {
                if (s < 1e-12)
                {
                    return u > 0 ? u : 0;
                }
                double x = u / s;
                return u * F(x) - s * M(x);
            }

            // sample visible slope p along the incidence plane (unit roughness), u = cos, s = sin
            public double SampleP(double u, double s, double xi)
            {
                if (s < 1e-12)
                {
                    // normal incidence: slope marginal itself
                    double lo0 = -1, hi0 = 1;
                    while (F(lo0) > xi)
                    {
                        lo0 *= 2;
                    }
                    while (F(hi0) < xi)
                    {
                        hi0 *= 2;
                    }
                    for (int i = 0; i < 200; ++i)
                    {
                        double mid = 0.5 * (lo0 + hi0);
                        if (F(mid) < xi)
                        {
                            lo0 = mid;
                        }
                        else
                        {
                            hi0 = mid;
                        }
                    }
                    return 0.5 * (lo0 + hi0);
                }
                double x = u / s;
                double sigma = u * F(x) - s * M(x);
                double target = xi * sigma;
                double hi = x, lo = x - 1;
                for (int i = 0; i < 400 && (u * F(lo) - s * M(lo)) > target; ++i)
                {
                    lo = x - 2 * (x - lo);
                }
                double p = 0.5 * (lo + hi);
                for (int it = 0; it < 200; ++it)
                {
                    double g = (u * F(p) - s * M(p)) - target;
                    if (g > 0)
                    {
                        hi = p;
                    }
                    else
                    {
                        lo = p;
                    }
                    double d = (u - s * p) * f(p);
                    double pn = (d > 0) ? p - g / d : 0.5 * (lo + hi);
                    if (!(pn > lo && pn < hi))
                    {
                        pn = 0.5 * (lo + hi);
                    }
                    if (Math.Abs(pn - p) < 1e-14 * (1 + Math.Abs(p)) || hi - lo < 1e-15 * (1 + Math.Abs(p)))
                    {
                        p = pn;
                        break;
                    }
                    p = pn;
                }
                return p;
            }

            // sample q | p by inverting the conditional CDF with u (fully stratifiable)
            public double SampleQInv(double p, double u)
            {
                if (beckmann)
                {
                    return InvNormCdf(u) * 0.7071067811865476;
                }
                double nu = 2 * gamma - 1;
                return StudentQuantile(nu, u) * Math.Sqrt((gamma - 1 + p * p) / nu);
            }

            // sample q | p
            public double SampleQ(double p, Rnd rnd)
            {
                double z = rnd.Normal();
                if (beckmann)
                {
                    return z * 0.7071067811865476;
                }
                double chi2 = 2 * rnd.Gamma(gamma - 0.5);
                return z * Math.Sqrt((gamma - 1 + p * p) / chi2);
            }
        }

        private static readonly double[] invNormA = { -3.969683028665376e+01, 2.209460984245205e+02, -2.759285104469687e+02,
                                                      1.383577518672690e+02, -3.066479806614716e+01, 2.506628277459239e+00 };
        private static readonly double[] invNormB = { -5.447609879822406e+01, 1.615858368580409e+02, -1.556989798598866e+02,
                                                      6.680131188771972e+01, -1.328068155288572e+01 };
        private static readonly double[] invNormC = { -7.784894002430293e-03, -3.223964580411365e-01, -2.400758277161838e+00,
                                                      -2.549732539343734e+00, 4.374664141464968e+00, 2.938163982698783e+00 };
        private static readonly double[] invNormD = { 7.784695709041462e-03, 3.224671290700398e-01, 2.445134137142996e+00,
                                                      3.754408661907416e+00 };

        // inverse CDF of the standard normal (Acklam + one Halley step, ~1e-15)
        public static double InvNormCdf(double u)
        {
            double[] a = invNormA, b = invNormB, c = invNormC, d = invNormD;
            u = Math.Min(Math.Max(u, 1e-300), 1.0 - 1e-16);
            double x;
            if (u < 0.02425)
            {
                double q = Math.Sqrt(-2 * Math.Log(u));
                x = (((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) / ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1);
            }
            else if (u > 1 - 0.02425)
            {
                double q = Math.Sqrt(-2 * Log1p(-u));
                x = -(((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) / ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1);
            }
            else
            {
                double q = u - 0.5, r = q * q;
                x = (((((a[0] * r + a[1]) * r + a[2]) * r + a[3]) * r + a[4]) * r + a[5]) * q /
                    (((((b[0] * r + b[1]) * r + b[2]) * r + b[3]) * r + b[4]) * r + 1);
            }
            double e = 0.5 * pbrt.Erfc(-x / 1.4142135623730951) - u;
            double dd = e * 2.5066282746310002 * Math.Exp(0.5 * x * x);
            return x - dd / (1 + 0.5 * x * dd);
        }

        // quantile of Student's t with nu degrees of freedom (via the incomplete beta, safeguarded Newton)
        public static double StudentQuantile(double nu, double u)
        {
            if (u == 0.5)
            {
                return 0;
            }
            double tail = u < 0.5 ? u : 1 - u;  // P(T > t) = tail for t >= 0
            double target = 2 * tail;            // I_x(nu/2, 1/2) with x = nu/(nu+t^2)
            double a = nu / 2, b = 0.5;
            double lbeta = pbrt.LogGamma(a) + pbrt.LogGamma(b) - pbrt.LogGamma(a + b);
            double lo = 0, hi = 1, x = 0.5;
            for (int it = 0; it < 200; ++it)
            {
                double v = IncBeta(a, b, x) - target;
                if (v > 0)
                {
                    hi = x;
                }
                else
                {
                    lo = x;
                }
                double dens = Math.Exp((a - 1) * Math.Log(x) + (b - 1) * Log1p(-x) - lbeta);
                double xn = dens > 0 ? x - v / dens : 0.5 * (lo + hi);
                if (!(xn > lo && xn < hi))
                {
                    xn = 0.5 * (lo + hi);
                }
                if (Math.Abs(xn - x) < 1e-15 * x || hi - lo < 1e-300)
                {
                    x = xn;
                    break;
                }
                x = xn;
            }
            double t = Math.Sqrt(nu * (1 - x) / x);
            return u < 0.5 ? -t : t;
        }

        // reference code's fitted sigma/u for Student-T (for comparison tests only)
        public static double RefAuxF(double u, double g)
        {
            return Math.Atan(2.00141 - 1.6253863790572571 * g) *
                   Math.Sin(0.993127 * (-1.00658 + u - (0.0209307 * (-2.63062 + g) * u) / (2.19417 + g)) * Math.Tan(u));
        }

        public static double RefAuxF2(double x, double g)
        {
            return 1 + 1 / pbrt.Erf(RefAuxF(x / Math.Sqrt(1 + x * x), g) / (1 - x / Math.Sqrt(1 + x * x)));
        }

        public static double StudentTSigmaReferenceFit(double u, double alphaI, double gamma)
        {
            if (u > 0.9999)
            {
                return 1;
            }
            if (u < -0.9999)
            {
                return 0;
            }
            double th = Math.Acos(u);
            double x = 1.0 / Math.Tan(th) / alphaI;
            if (Math.Abs(u) < 1e-12)
            {
                return alphaI * Math.Sqrt(gamma - 1) * TGamma(gamma - 1.5) / (2 * Math.Sqrt(ffPi) * TGamma(gamma - 1));
            }
            return u > 0 ? 0.5 * u * RefAuxF2(x, gamma) : -0.5 * u * RefAuxF2(-x, gamma) + u;
        }

        // ------------------------------------------------------------------------------------------
        // paper's Beckmann-superposition sampler (fitted; approximate): verbatim from reference code
        // ------------------------------------------------------------------------------------------
        // probability mass of the first term in the 3-term m' Beckmann superposition
        public static double PaperPTerm1(double u, double gamma)
        {
            return (3 * Math.Sqrt(3 - 3 * Math.Pow(u, 2))) /
                   (Math.Pow(1 + Math.Pow(u, 2) / (3 - 3 * Math.Pow(u, 2)), 2.5) *
                    (8 * u - (81 * Math.Pow(-1 + Math.Pow(u, 2), 3)) / Math.Pow(3 - 2 * Math.Pow(u, 2), 2.5) -
                     (Math.Pow(u, 2) * Math.Sqrt(3 - 2 * Math.Pow(u, 2)) * Math.Sqrt((-1 + Math.Pow(u, 2)) / (-3 + 2 * Math.Pow(u, 2))) *
                      (135 - 210 * Math.Pow(u, 2) + 83 * Math.Pow(u, 4))) /
                         (Math.Pow(-1 + Math.Pow(u, 2), 3) * Math.Pow((-3 + 2 * Math.Pow(u, 2)) / (-1 + Math.Pow(u, 2)), 2.5))));
        }

        // probability mass of the second term in the 3-term m' Beckmann superposition
        public static double PaperPTerm2(double u, double x)
        {
            return Math.Pow(u, 0.809494 + 0.170783 / (-1.1224 + x)) /
                   (1 + Math.Pow(u, 0.145598 + 0.000805627 * x + Math.Atan(1.05504 * (-2.91109 + Math.Pow(x, 2)))));
        }

        // Beckmann superposition sampling of Student-T: sample angle-dependent Beckmann roughness
        public static double PaperSampleMPrime(double u, double gamma, Rnd rnd)
        {
            if (u < 0.0)
            {
                // approximate method using fitted gen gamma distribution
                double y = gamma;
                double a = -1.49293 + y - (0.0655156 * (0.0442664 + u)) / (1.20697 + Math.Cos(0.633638 + y));
                double c = 1.00448 + (0.0138041 + u) / (-0.850096 - u + 0.516877 * Math.Pow(y, 2) - Math.Asinh(u));

                // {2,2}-Pade approximant of full m1 at u = 0
                double m1 = (Math.Sqrt(-1 + y) * (-3 + 2 * y) * (-6 * Math.Pow(ffPi, 1.5) * u * Math.Pow(-1 + y, 3.5) * (-4 + 3 * y) * Math.Pow(TGamma(-1 + y), 3) + 4 * (4 * Math.Pow(-1 + y, 2) * (-3 + Math.Pow(u, 2) + (3 + Math.Pow(u, 2)) * y) * Math.Pow(TGamma(-0.5 + y), 3) + Math.Pow(2, 3 - 2 * y) * ffPi * u * Math.Pow(-1 + y, 2.5) * (-14 + 13 * y) * TGamma(-0.5 + y) * TGamma(-2 + 2 * y) + (Math.Pow(ffPi, 1.5) * (-6 * (-1 + y) * (-4 + 3 * y) + Math.Pow(u, 2) * (8 - 5 * Math.Pow(y, 2))) * TGamma(y) * TGamma(-1 + 2 * y)) / Math.Pow(4, y)))) /
                            (2.0 * (16 * (Math.Pow(u, 2) * (-2 + y) + 3 * (-1 + y)) * Math.Pow(-1 + y, 2.5) *
                                        Math.Pow(TGamma(-0.5 + y), 3) +
                                    Math.Pow(2, 5 - 2 * y) * ffPi * u * Math.Pow(-1 + y, 3) * (-20 + 13 * y) *
                                        TGamma(-0.5 + y) * TGamma(-2 + 2 * y) +
                                    Math.Pow(ffPi, 1.5) * TGamma(y) * (-3 * u * (-3 + 2 * y) * (-4 + 3 * y) * Math.Pow(TGamma(y), 2) - Math.Pow(2, 3 - 2 * y) * Math.Pow(-1 + y, 1.5) * (6 * (-1 + y) * (-4 + 3 * y) + Math.Pow(u, 2) * (-2 + y) * (-6 + 5 * y)) * TGamma(-2 + 2 * y))));

                double b = m1 * TGamma(a) / TGamma(a + 1 / c);

                return Math.Pow(rnd.Gamma(a), 1.0 / c) * b;
            }

            double xi1 = rnd.U();
            double p1 = PaperPTerm1(u, gamma);
            double p2 = PaperPTerm2(u, gamma);

            double gamma_width = 1 / (1 - Math.Pow(u, 2) / ((-1 + gamma) * (-1 + Math.Pow(u, 2))));

            if (xi1 < p1)
            {
                // term 1
                return rnd.Gamma(-1.5 + gamma) * gamma_width;
            }
            else
            {
                if (xi1 < p1 + p2)
                {
                    // term 2
                    return rnd.Gamma(gamma - 1.0);
                }
                else
                {
                    // term 3
                    double m = rnd.Gamma(gamma - 1.0);
                    while (rnd.U() > pbrt.Erf(u * Math.Sqrt(-(m / ((-1 + gamma) * (-1 + Math.Pow(u, 2)))))))
                    {
                        m = rnd.Gamma(gamma - 1.0);
                    }
                    return m;
                }
            }
        }

        // ------------------------------------------------------------------------------------------
        // null-scattering (full-sphere) NDFs
        // ------------------------------------------------------------------------------------------
        public static double DiracSigma(double u, double un)
        {
            double result = 0.0;
            if (u * un > 0.0)
            {
                result += 2 * ffPi * u * un;
            }
            if (Math.Abs(u) < Math.Sqrt(Math.Max(0.0, 1.0 - un * un)))
            {
                result += 2.0 * Math.Sqrt(Math.Max(0.0, -un * un - u * u + 1));
                double den = Math.Sqrt((1 - u * u) * (1 - un * un));
                double arg = den > 0 ? Math.Min(1.0, Math.Max(-1.0, -u * un / den)) : 0;
                result += 2 * u * un * Math.Acos(arg);
                if (u * un > 0.0)
                {
                    result -= 2 * ffPi * u * un;
                }
            }
            return result;
        }

        public static void GaussLegendre16(double[] x, double[] w)
        {
            const int n = 16;
            for (int i = 0; i < n; ++i)
            {
                double z = Math.Cos(ffPi * (i + 0.75) / (n + 0.5)), pp = 1;
                for (int it = 0; it < 100; ++it)
                {
                    double p1 = 1, p2 = 0;
                    for (int j = 0; j < n; ++j)
                    {
                        double p3 = p2;
                        p2 = p1;
                        p1 = ((2.0 * j + 1) * z * p2 - j * p3) / (j + 1);
                    }
                    pp = n * (z * p1 - p2) / (z * z - 1);
                    double z1 = z;
                    z = z1 - p1 / pp;
                    if (Math.Abs(z - z1) < 1e-15)
                    {
                        break;
                    }
                }
                x[i] = z;
                w[i] = 2 / ((1 - z * z) * pp * pp);
            }
        }

        // Shared (immutable) per-null-NDF data: physical density D_phys(u) on the full sphere, majorant M >= pi * sup D_phys
        // (free-path rate of the constant-majorant process, sigma_t = M), and the tabulated exact cross section. The vMF NDF is
        // the reference code's (D/Dbar = exp(2(u-1)/a^2) with Dbar = 1/pi, i.e. M = 1). The null Student-T NDF is the *ordinary*
        // Student-T NDF (normalised heightfield) realised through the null-scattering machinery.
        public sealed class NullTable
        {
            public const int N = 1025;
            public Kind kind = Kind.VMF;
            public double alpha = 1, gamma = 2, M = 1;
            public double[] sig = new double[N];

            // physical density
            public double Dphys(double u)
            {
                if (kind == Kind.VMF)
                {
                    return Math.Exp(2.0 * (u - 1.0) / (alpha * alpha)) / ffPi;
                }
                if (u <= 0 || u > 1)
                {
                    return 0;
                }
                double s = 1.0 / (Math.Pow(u, 4) * Math.Pow(1 + (1 - u * u) / (u * u * alpha * alpha * (gamma - 1)), gamma));
                return s / (ffPi * alpha * alpha);
            }

            // acceptance probability D/Dbar of the null-collision process
            public double Drel(double u) { return Math.Min(1.0, ffPi * Dphys(u) / M); }

            public void Init(Kind k, double a, double g)
            {
                kind = k;
                alpha = a;
                gamma = g;
                M = 1;
                if (k == Kind.NullStudentT)
                {
                    double mx = 0;
                    for (int i = 1; i <= 40000; ++i)
                    {
                        double u = i / 40000.0;
                        mx = Math.Max(mx, Dphys(u));
                    }
                    M = ffPi * mx * 1.0005;
                }
                for (int i = 0; i < N; ++i)
                {
                    sig[i] = ComputeSigma(-1.0 + 2.0 * i / (N - 1));
                }
            }

            public double ComputeSigma(double u)
            {
                double[] gx = new double[16], gw = new double[16];
                GaussLegendre16(gx, gw);
                double s = Math.Sqrt(Math.Max(0.0, 1 - u * u));
                double[] e = { -1, -s, 0, s, 1, 0.995, 0.98, 0.94, 0.85, 0.65, 0.25 };
                List<double> sorted = new List<double>(e);
                sorted.Sort();
                List<double> edges = new List<double>();
                foreach (double value in sorted)
                {
                    if (edges.Count == 0 || !(Math.Abs(edges[edges.Count - 1] - value) < 1e-14))
                    {
                        edges.Add(value);
                    }
                }
                double sum = 0;
                for (int i = 0; i + 1 < edges.Count; ++i)
                {
                    double a = edges[i], b = edges[i + 1];
                    if (b - a < 1e-14)
                    {
                        continue;
                    }
                    for (int j = 0; j < 16; ++j)
                    {
                        double un = 0.5 * (a + b) + 0.5 * (b - a) * gx[j];
                        sum += 0.5 * (b - a) * gw[j] * Dphys(un) * DiracSigma(u, un);
                    }
                }
                return sum;
            }

            // Catmull-Rom interpolation of the table
            public double Sigma(double u)
            {
                u = Math.Min(1.0, Math.Max(-1.0, u));
                double t = (u + 1) * 0.5 * (N - 1);
                int i = Math.Min(N - 2, (int)t);
                double f = t - i;
                double p0 = sig[Math.Max(0, i - 1)], p1 = sig[i], p2 = sig[i + 1], p3 = sig[Math.Min(N - 1, i + 2)];
                return 0.5 * ((2 * p1) + (-p0 + p2) * f + (2 * p0 - 5 * p1 + 4 * p2 - p3) * f * f + (-p0 + 3 * p1 - 3 * p2 + p3) * f * f * f);
            }
        }

        // ------------------------------------------------------------------------------------------
        // one NDF component
        // ------------------------------------------------------------------------------------------
        public sealed class Comp
        {
            public Kind kind = Kind.GGX;
            public double w = 1;                 // mixture weight (normalised)
            public double ax = 0.5, ay = 0.5;    // heightfield roughness
            public double gamma = 2;             // Student-T shape
            public SlopeDist sd;                 // heightfield slope marginals
            public NullTable tab = null;         // null NDFs

            public Comp Clone()
            {
                return (Comp)MemberwiseClone();
            }

            public bool IsNull() { return kind == Kind.VMF || kind == Kind.NullStudentT; }

            public void InitHeightfield(Kind k, double rx, double ry, double g)
            {
                kind = k;
                ax = Math.Max(rx, 1e-4);
                ay = Math.Max(ry, 1e-4);
                gamma = (k == Kind.GGX) ? 2.0 : g;
                sd.Init(k, gamma);
            }

            public double AlphaI(V3 wi)
            {
                double s2 = Math.Max(1e-30, 1 - wi.z * wi.z);
                double c2 = wi.x * wi.x / s2, sn2 = wi.y * wi.y / s2;
                return Math.Sqrt(c2 * ax * ax + sn2 * ay * ay);
            }

            // normal distribution, normalised so that int cos D dw = 1 (heightfield);
            // null NDFs: D_phys = D_rel / pi.
            public double D(V3 m)
            {
                if (m.z <= 0 && !IsNull())
                {
                    return 0;
                }
                if (IsNull())
                {
                    return tab.Dphys(m.z);
                }
                double px = -m.x / m.z, py = -m.y / m.z;
                double r2 = px * px / (ax * ax) + py * py / (ay * ay);
                double P22;
                if (kind == Kind.Beckmann)
                {
                    P22 = Math.Exp(-r2) / (ffPi * ax * ay);
                }
                else
                {
                    P22 = Math.Pow((gamma - 1) / (gamma - 1 + r2), gamma) / (ffPi * ax * ay);
                }
                double u2 = m.z * m.z;
                return P22 / (u2 * u2);
            }

            // projected area / cross section
            public double Sigma(V3 wi)
            {
                if (IsNull())
                {
                    return tab.Sigma(wi.z);
                }
                if (wi.z > 1 - 1e-13)
                {
                    return 1.0;
                }
                if (wi.z < -(1 - 1e-13))
                {
                    return 0.0;
                }
                double s = Math.Sqrt(1 - wi.z * wi.z);
                return sd.Sigma(wi.z, s * AlphaI(wi));  // sigma = u F(x) - s a M(x) with x = u/(s a)
            }

            // Exact heightfield visible-normal sampling, deterministic and fully stratified: u0 inverts the slope CDF along the
            // incidence plane, u1 inverts the conditional transverse slope CDF.
            public V3 SampleVisibleU(V3 wi, double u0, double u1)
            {
                V3 ws = ff.Normalize(new V3(ax * wi.x, ay * wi.y, wi.z));
                double u = ws.z, s = Math.Sqrt(Math.Max(0.0, 1 - u * u));
                double phi = Math.Atan2(ws.y, ws.x);
                double p = sd.SampleP(u, s, u0);
                double q = sd.SampleQInv(p, u1);
                double c = Math.Cos(phi), sn = Math.Sin(phi);
                double sx = (c * p - sn * q) * ax, sy = (sn * p + c * q) * ay;
                V3 m = ff.Normalize(new V3(-sx, -sy, 1));
                if (!Double.IsFinite(m.x + m.y + m.z))
                {
                    return new V3(0, 0, 1);
                }
                return m;
            }

            // visible normal sampling, exact
            public V3 SampleVisible(V3 wi, Rnd rnd, bool paperSampler)
            {
                if (IsNull())
                {
                    for (int it = 0; it < 200000; ++it)
                    {
                        // uniform disk perpendicular to wi -> normals facing wi
                        V3 a, b;
                        Basis(wi, out a, out b);
                        double r = Math.Sqrt(rnd.U()), ph = 2 * ffPi * rnd.U();
                        double dx = r * Math.Cos(ph), dy = r * Math.Sin(ph);
                        V3 mn = dx * a + dy * b + Math.Sqrt(Math.Max(0.0, 1 - r * r)) * wi;
                        if (rnd.U() < tab.Drel(mn.z))
                        {
                            return mn;
                        }
                    }
                    return wi;
                }
                double rx = ax, ry = ay;
                SlopeDist sdl = sd;
                if (kind == Kind.StudentT && paperSampler)
                {
                    // paper's sampler: pick Beckmann roughness m' from fitted superposition, then Beckmann vNDF
                    double mp = PaperSampleMPrime(wi.z, gamma, rnd);
                    double br = 1.0 / Math.Sqrt(mp / (gamma - 1.0));
                    rx *= br;
                    ry *= br;
                    sdl.Init(Kind.Beckmann, 0);
                }
                // stretch
                V3 ws = ff.Normalize(new V3(rx * wi.x, ry * wi.y, wi.z));
                double u = ws.z, s = Math.Sqrt(Math.Max(0.0, 1 - u * u));
                double phi = Math.Atan2(ws.y, ws.x);
                double p = sdl.SampleP(u, s, rnd.U());
                double q = sdl.SampleQ(p, rnd);
                double c = Math.Cos(phi), sn = Math.Sin(phi);
                double sx = (c * p - sn * q) * rx, sy = (sn * p + c * q) * ry;
                V3 m = ff.Normalize(new V3(-sx, -sy, 1));
                if (!Double.IsFinite(m.x + m.y + m.z))
                {
                    return wi.z > 0 ? new V3(0, 0, 1) : ff.Normalize(new V3(wi.x, wi.y, 1e-6));
                }
                return m;
            }
        }

        // ------------------------------------------------------------------------------------------
        // NDF = weighted blend of components
        // ------------------------------------------------------------------------------------------
        public sealed class Ndf
        {
            public int n = 0;
            public Comp[] c = new Comp[kMaxComps] { new Comp(), new Comp(), new Comp(), new Comp() };
            public bool paperSampler = false;
            public bool literalNull = false;
            // Asymmetric (layered Smith volume) operator of d'Eon et al. 2023, "Microfacet theory for non-uniform heightfields":
            // the n components are strata ordered from the outermost (top) down, component i has height-probability mass w_i, so
            // with C = cumulative height distribution the stratum boundaries in the optical-depth coordinate z = log C are
            // z_j = log(1 - sum_{k<=j} w_k). Refraction flips the volume: layers are reversed (flip = true) and
            // z -> log(1 - exp z).
            public bool layered = false;

            public Ndf Clone()
            {
                Ndf r = (Ndf)MemberwiseClone();
                r.c = new Comp[kMaxComps];
                for (int i = 0; i < kMaxComps; ++i)
                {
                    r.c[i] = c[i].Clone();
                }
                return r;
            }

            public int LayerIdx(int j, bool flip) { return flip ? n - 1 - j : j; }

            // lower boundary (optical-depth coordinate) of stratum j (j < n-1), in the oriented order
            public double LayerBoundary(int j, bool flip)
            {
                double cum = 0;
                for (int k = 0; k <= j; ++k)
                {
                    cum += c[LayerIdx(k, flip)].w;
                }
                return Math.Log(Math.Max(1e-300, 1.0 - cum));
            }

            // vNDF of the layer that produced a collision (layer < 0: the blended NDF)
            public double DwiLayer(int layer, V3 wi, V3 wm)
            {
                if (!layered || layer < 0)
                {
                    return Dwi(wi, wm);
                }
                double sg = c[layer].Sigma(wi);
                if (sg == 0)
                {
                    return 0;
                }
                return Math.Max(0.0, Dot(wi, wm)) * c[layer].D(wm) / sg;
            }

            // transmittance from depth h0 <= 0 (in the oriented frame) along upward direction wi
            public double G1Layered(V3 wi, double h0, bool flip)
            {
                if (wi.z <= 0)
                {
                    return 0;
                }
                if (h0 >= 0)
                {
                    return 1;
                }
                double od = 0, top = 0;
                for (int j = 0; j < n; ++j)
                {
                    double bottom = (j == n - 1) ? -1e300 : LayerBoundary(j, flip);
                    double lo = Math.Max(h0, bottom);
                    if (lo < top)
                    {
                        od += (top - lo) * c[LayerIdx(j, flip)].Sigma(-wi);
                    }
                    if (h0 >= bottom)
                    {
                        break;
                    }
                    top = bottom;
                }
                return Math.Exp(-od / wi.z);
            }

            // free flight through the layered volume; optical depth is consumed layer by layer (extinction sigma_j(-wr)/|wr.z|
            // per unit |dz| in stratum j). Returns the new z (>= 0 on escape); on collision (z < 0) sets wm (visible normal of
            // the stratum) and layer.
            public double SampleHeightLayered(V3 wr, double hr, bool flip, Rnd rnd, ref V3 wm, ref int layer)
            {
                double z = Math.Min(0.0, hr);
                double xi = -Log1p(-rnd.U());
                bool up = wr.z > 0;
                double az = Math.Abs(wr.z);
                int j = 0;
                for (; j < n - 1; ++j)
                {
                    if (z > LayerBoundary(j, flip) || (up && z >= LayerBoundary(j, flip)))
                    {
                        break;
                    }
                }
                for (int guard = 0; guard < 64; ++guard)
                {
                    Comp cj = c[LayerIdx(j, flip)];
                    double st = cj.Sigma(-wr);
                    double top = (j == 0) ? 0.0 : LayerBoundary(j - 1, flip);
                    double bottom = (j == n - 1) ? -1e300 : LayerBoundary(j, flip);
                    double target = up ? top : bottom;
                    if (st >= 1e-5)
                    {
                        double dz = xi * az / st;
                        double zc = up ? z + dz : z - dz;
                        bool inside = up ? (zc < target) : (zc > target);
                        if (inside || (!up && j == n - 1))
                        {
                            if (zc >= 0.0)
                            {
                                return zc;  // (cannot happen: up && zc < top <= 0)
                            }
                            wm = cj.SampleVisible(-wr, rnd, paperSampler);
                            layer = LayerIdx(j, flip);
                            return zc;
                        }
                        xi -= Math.Abs(target - z) * st / az;
                    }
                    else if (!up && j == n - 1)
                    {
                        wm = new V3(0, 0, 1);
                        layer = LayerIdx(j, flip);
                        return z;
                    }
                    z = target;
                    if (up)
                    {
                        if (j == 0)
                        {
                            return 0.0;  // escaped through the surface
                        }
                        --j;
                    }
                    else
                    {
                        ++j;
                    }
                }
                return 0.0;
            }

            public void Normalize()
            {
                double s = 0;
                for (int i = 0; i < n; ++i)
                {
                    s += c[i].w;
                }
                for (int i = 0; i < n; ++i)
                {
                    c[i].w /= s;
                }
            }

            public double D(V3 m)
            {
                double d = 0;
                for (int i = 0; i < n; ++i)
                {
                    d += c[i].w * c[i].D(m);
                }
                return d;
            }

            public double Sigma(V3 wi)
            {
                double s = 0;
                for (int i = 0; i < n; ++i)
                {
                    s += c[i].w * c[i].Sigma(wi);
                }
                return s;
            }

            public double Dwi(V3 wi, V3 wm)
            {
                double sg = Sigma(wi);
                if (sg == 0)
                {
                    return 0;
                }
                return Math.Max(0.0, Dot(wi, wm)) * D(wm) / sg;
            }

            public V3 SampleVisible(V3 wi, Rnd rnd)
            {
                if (n == 1)
                {
                    return c[0].SampleVisible(wi, rnd, paperSampler);
                }
                double* sg = stackalloc double[kMaxComps];
                double tot = 0;
                for (int i = 0; i < n; ++i)
                {
                    sg[i] = c[i].w * c[i].Sigma(wi);
                    tot += sg[i];
                }
                double xi = rnd.U() * tot, acc = 0;
                for (int i = 0; i < n; ++i)
                {
                    acc += sg[i];
                    if (xi < acc || i == n - 1)
                    {
                        return c[i].SampleVisible(wi, rnd, paperSampler);
                    }
                }
                return new V3(0, 0, 1);
            }

            // transmittance from depth h0 <= 0 along upward direction wi
            public double G1(V3 wi, double h0, bool flip)
            {
                if (layered && n > 1)
                {
                    return G1Layered(wi, h0, flip);
                }
                if (wi.z <= 0)
                {
                    return 0;
                }
                if (h0 >= 0)
                {
                    return 1;
                }
                return Math.Exp(h0 / wi.z * Sigma(-wi));
            }

            // free path: returns new height; on collision (h<0) sets wm
            public double SampleHeight(V3 wr, double hr, Rnd rnd, ref V3 wm, bool flip, ref int layer)
            {
                if (layered && n > 1)
                {
                    return SampleHeightLayered(wr, hr, flip, rnd, ref wm, ref layer);
                }
                layer = -1;
                if (literalNull && n == 1 && c[0].IsNull())
                {
                    // literal null-collision walk of the reference code (constant majorant, sigma_t = 1)
                    double hn = Math.Min(0.0, hr) - Log1p(-rnd.U()) * wr.z / c[0].tab.M;
                    while (hn <= 0.0)
                    {
                        V3 wi = -wr, a, b;
                        Basis(wi, out a, out b);
                        double r = Math.Sqrt(rnd.U()), ph = 2 * ffPi * rnd.U();
                        V3 m = r * Math.Cos(ph) * a + r * Math.Sin(ph) * b + Math.Sqrt(Math.Max(0.0, 1 - r * r)) * wi;
                        if (rnd.U() < c[0].tab.Drel(m.z))
                        {
                            wm = m;
                            return hn;
                        }
                        hn += -Log1p(-rnd.U()) * wr.z / c[0].tab.M;
                    }
                    return hn;
                }
                double st = Sigma(-wr);
                if (st < 1e-5)
                {
                    return (wr.z < 0.0) ? hr : 0.0;
                }
                double dh = -Log1p(-rnd.U()) * wr.z / st;
                double h = Math.Min(0.0, hr) + dh;
                if (h < 0.0)
                {
                    wm = SampleVisible(-wr, rnd);
                }
                return h;
            }
        }

        // ------------------------------------------------------------------------------------------
        // thin film (util/thinfilm.h and FrThinFilm() of util/scattering.h of the fork)
        // ------------------------------------------------------------------------------------------
        // Unpolarized reflectance of a thin non-absorbing film (index eta2, thickness d in nm) between a medium of real index
        // eta1 and a base of complex index eta3 + i k3: the exact Airy summation.
        public static Float FrThinFilm(Float cosTheta1, Float lambdaNm, Float d, Float eta1, Float eta2, Float eta3, Float k3)
        {
            cosTheta1 = pbrt.Clamp(cosTheta1, 0, 1);
            double sin2Theta1 = 1 - (double)cosTheta1 * cosTheta1;
            // Snell's law into the film (real index)
            double sin2Theta2 = sin2Theta1 * ((double)eta1 / eta2) * ((double)eta1 / eta2);
            if (sin2Theta2 >= 1)
            {
                return 1;  // total internal reflection at the first interface
            }
            double cosTheta2 = Math.Sqrt(Math.Max(0.0, 1 - sin2Theta2));
            // Complex cos(theta3) in the base
            Complex n3 = new Complex(eta3, k3);
            Complex sin2Theta3 = ((double)eta2 * eta2 * sin2Theta2) / (n3 * n3);
            Complex cosTheta3 = Complex.Sqrt(Complex.One - sin2Theta3);
            // Fresnel amplitude coefficients
            double r12s = (eta1 * cosTheta1 - eta2 * cosTheta2) / (eta1 * cosTheta1 + eta2 * cosTheta2);
            double r12p = (eta2 * cosTheta1 - eta1 * cosTheta2) / (eta2 * cosTheta1 + eta1 * cosTheta2);
            Complex r23s = (eta2 * cosTheta2 - n3 * cosTheta3) / (eta2 * cosTheta2 + n3 * cosTheta3);
            Complex r23p = (n3 * cosTheta2 - eta2 * cosTheta3) / (n3 * cosTheta2 + eta2 * cosTheta3);
            // Phase accumulated by a round trip through the film
            double delta = 4 * Math.PI * eta2 * d * cosTheta2 / lambdaNm;
            Complex e = new Complex(Math.Cos(delta), Math.Sin(delta));
            Complex rs = (r12s + r23s * e) / (Complex.One + r12s * r23s * e);
            Complex rp = (r12p + r23p * e) / (Complex.One + r12p * r23p * e);
            return (Float)((rs.Real * rs.Real + rs.Imaginary * rs.Imaginary + rp.Real * rp.Real + rp.Imaginary * rp.Imaginary) / 2);
        }

        // Thin non-absorbing dielectric film on a conductor or dielectric base. Reflectance is the exact Airy summation (all
        // internal reflections, both polarizations, complex base index) evaluated at each sampled wavelength, so it is unbiased
        // and converges with samples. `weight` blends between the BSDF without (0) and with (1) the film. On a dielectric base
        // transmission through the film is T = 1 - R (the film does not absorb).
        public struct ThinFilm
        {
            public Float thickness;  // nm
            public Float index;      // film index relative to the surrounding medium
            public Float weight;     // 0 = no film, 1 = full film
            public SampledWavelengths lambda;

            public bool Enabled() { return thickness > 0 && weight > 0; }

            // Unpolarized reflectance of film-over-base (index baseN + i baseK at the sampled wavelengths) seen from a medium of
            // index n1 (real), blended with the filmless reflectance R0 by `weight`.
            public SampledSpectrum Reflectance(Float cosTheta1, Float n1, SampledSpectrum baseN, SampledSpectrum baseK, SampledSpectrum R0)
            {
                SampledSpectrum R = new SampledSpectrum(0f);
                for (int i = 0; i < pbrt.NSpectrumSamples; ++i)
                {
                    R[i] = FrThinFilm(cosTheta1, lambda[i], thickness, n1, index, baseN[i], baseK[i]);
                }
                if (weight >= 1)
                {
                    return R;
                }
                return R0 * (1 - weight) + R * weight;
            }
        }

        // ------------------------------------------------------------------------------------------
        // facets and surfaces (recursive)
        // ------------------------------------------------------------------------------------------
        public enum FacetKind
        {
            Conductor,
            Dielectric,
            Diffuse,
            Mirror
        }

        public static double DielectricR(double cosI, double eta)
        {
            double st = eta * eta - 1.0 + cosI * cosI;
            if (st <= 0)
            {
                return 1.0;
            }
            double g = Math.Sqrt(st), c = cosI;
            return (Math.Pow(-c + g, 2) * (1 + Math.Pow(-1 + c * (c + g), 2) / Math.Pow(1 + c * (-c + g), 2))) / (2.0 * Math.Pow(c + g, 2));
        }

        public sealed class Facet
        {
            public FacetKind kind = FacetKind.Mirror;
            public SampledSpectrum eta = new SampledSpectrum(0f), k = new SampledSpectrum(0f);  // conductor, relative to the (outer) medium of ior 1
            public SampledSpectrum R = new SampledSpectrum(0f);                                 // diffuse / mirror reflectance
            public ThinFilm film;    // optional thin dielectric film on a conductor / dielectric facet
            // (ours) The films of Magi instead (ThinFilmLayers: any number of layers, with their dispersion), when hasStack
            public ThinFilmStack stack;
            public bool hasStack = false;

            public bool Filmed() { return hasStack || film.Enabled(); }
            public Float weight = 1; // conductor: scalar on the facet reflectance

            public Facet Clone()
            {
                return (Facet)MemberwiseClone();
            }
        }

        // Facet Fresnel reflectance (unpolarized), with the exact Airy thin-film summation when a film is present. The film sits
        // on the facet: medium iorI above, base (conductor eta/k, or dielectric iorT) below; for a ray inside the dielectric the
        // stack is read from the other side.
        public static SampledSpectrum FacetFrConductor(Facet f, double cosI, double iorI)
        {
            if (f.hasStack)
            {
                f.stack.Conductor((Float)Math.Max(0.0, cosI), f.eta, f.k, out SampledSpectrum Fs, out SampledSpectrum Fp, out SampledSpectrum _, out SampledSpectrum _);
                SampledSpectrum filmed = (Fs + Fp) * 0.5f;
                return f.weight == 1 ? filmed : filmed * f.weight;
            }
            SampledSpectrum R0 = pbrt.FrComplex((Float)Math.Max(0.0, cosI), f.eta / (Float)iorI, f.k / (Float)iorI);
            if (f.film.Enabled())
            {
                R0 = f.film.Reflectance((Float)Math.Max(0.0, cosI), (Float)iorI, f.eta, f.k, R0);
            }
            return f.weight == 1 ? R0 : R0 * f.weight;
        }

        public static SampledSpectrum FacetFrDielectric(Facet f, double cosI, double iorI, double iorT)
        {
            double R0 = DielectricR(cosI, iorT / iorI);
            if (f.hasStack)
            {
                // (the side as below; the stack takes the cosine with its sign, negative from the substrate)
                double index = (double)f.eta[0], ratio = iorT / iorI;
                bool outer = Math.Abs(ratio - index) <= Math.Abs(ratio - 1.0 / index);
                Float signed = (Float)Math.Max(1e-6, cosI) * (outer ? 1 : -1);
                f.stack.Dielectric(signed, (Float)index, out SampledSpectrum Fs, out SampledSpectrum Fp, out SampledSpectrum _, out SampledSpectrum _,
                                   out SampledSpectrum _, out SampledSpectrum _);
                return (Fs + Fp) * 0.5f;
            }
            if (!f.film.Enabled())
            {
                return new SampledSpectrum((Float)R0);
            }
            // The walk passes indices relative to the incident medium; the film needs absolute ones. f.eta[0] holds the absolute
            // dielectric ior (outer medium = 1): ratio ~ ior means the incident side is the outer medium, ratio ~ 1/ior means it
            // is the dense one.
            double ior = (double)f.eta[0], r = iorT / iorI;
            bool fromOuter = Math.Abs(r - ior) <= Math.Abs(r - 1.0 / ior);
            double n1 = fromOuter ? 1.0 : ior, n3 = fromOuter ? ior : 1.0;
            return f.film.Reflectance((Float)Math.Max(0.0, cosI), (Float)n1, new SampledSpectrum((Float)n3), new SampledSpectrum(0f),
                                      new SampledSpectrum((Float)R0));
        }

        public sealed class Surface
        {
            public Ndf ndf = new Ndf();
            public Facet facet = new Facet();  // used at the deepest level only
            // non-uniform material (paper 3.3.4): with a layered NDF each stratum may carry its own facet properties (same
            // FacetKind; conductor eta/k, diffuse/mirror reflectance); indexed like ndf.c
            public Facet[] layerFacet = new Facet[kMaxComps] { new Facet(), new Facet(), new Facet(), new Facet() };
            public bool perLayerFacet = false;
            public bool analyticSingle = true;  // closed-form first-order term (specular facets, single level)
            public int maxWalk = 32;

            public Surface Clone()
            {
                Surface r = (Surface)MemberwiseClone();
                r.ndf = ndf.Clone();
                r.facet = facet.Clone();
                r.layerFacet = new Facet[kMaxComps];
                for (int i = 0; i < kMaxComps; ++i)
                {
                    r.layerFacet[i] = layerFacet[i].Clone();
                }
                return r;
            }

            public Facet FacetFor(int layer)
            {
                return (perLayerFacet && layer >= 0) ? layerFacet[layer] : facet;
            }
        }

        // Surface seen from the other side (incident medium below): the strata of a layered NDF swap order (the first layer met
        // by a ray coming from inside is the bottom layer). Non-layered: unchanged.
        public static Surface ReverseLayers(Surface s)
        {
            Surface r = s.Clone();
            if (!(s.ndf.layered && s.ndf.n > 1))
            {
                return r;
            }
            for (int j = 0; j < s.ndf.n; ++j)
            {
                r.ndf.c[j] = s.ndf.c[s.ndf.n - 1 - j].Clone();
                r.layerFacet[j] = s.layerFacet[s.ndf.n - 1 - j].Clone();
            }
            return r;
        }

        public struct Stack
        {
            public Surface[] lv;
            public int n;

            public Stack(Surface[] lv, int n)
            {
                this.lv = lv;
                this.n = n;
            }
        }

        // facet BSDF sample in facet-local frame (z = facet normal), wi.z > 0
        public static bool FacetSample(Stack s, int d, double iorI, double iorT, V3 wi, ref SampledSpectrum w, bool unit, Rnd rnd, out V3 wo,
                                       int layer, Polar inner = null)
        {
            if (d + 1 < s.n)
            {
                return SampleLevel(s, d + 1, iorI, iorT, wi, ref w, unit, rnd, out wo, inner);
            }
            Facet f = s.lv[d].FacetFor(layer);
            switch (f.kind)
            {
            case FacetKind.Conductor:
            {
                if (!unit)
                {
                    w *= FacetFrConductor(f, wi.z, iorI);
                }
                wo = new V3(-wi.x, -wi.y, wi.z);
                return true;
            }
            case FacetKind.Dielectric:
            {
                double eta = iorT / iorI;
                SampledSpectrum Rs = FacetFrDielectric(f, wi.z, iorI, iorT);
                double FR = f.Filmed() ? (double)Rs.Average() : DielectricR(wi.z, eta);
                bool refl = rnd.U() <= FR;
                if (f.Filmed() && !unit)
                {
                    // spectral film: choose with the mean, weight per wavelength
                    if (refl)
                    {
                        w *= Rs / (Float)FR;
                    }
                    else
                    {
                        w *= (new SampledSpectrum(1f) - Rs) / (Float)(1.0 - FR);
                    }
                }
                if (refl)
                {
                    wo = new V3(-wi.x, -wi.y, wi.z);
                }
                else
                {
                    // refract: wi points away from surface; transmitted direction points into -z
                    double sin2 = Math.Max(0.0, 1 - wi.z * wi.z) / (eta * eta);
                    double cosT = Math.Sqrt(Math.Max(0.0, 1 - sin2));
                    wo = new V3(-wi.x / eta, -wi.y / eta, -cosT);
                }
                return true;
            }
            case FacetKind.Diffuse:
            {
                if (!unit)
                {
                    w *= f.R;
                }
                double r = Math.Sqrt(rnd.U()), ph = 2 * ffPi * rnd.U();
                wo = new V3(r * Math.Cos(ph), r * Math.Sin(ph), Math.Sqrt(Math.Max(0.0, 1 - r * r)));
                return true;
            }
            case FacetKind.Mirror:
            default:
                if (!unit)
                {
                    w *= f.R;
                }
                wo = new V3(-wi.x, -wi.y, wi.z);
                return true;
            }
        }

        // non-singular part of the facet BSDF times |cos wo|
        public static SampledSpectrum FacetEval(Stack s, int d, double iorI, double iorT, V3 wi, V3 wo, bool unit, Rnd rnd, int layer, Polar inner = null)
        {
            if (d + 1 < s.n)
            {
                return EvalLevel(s, d + 1, iorI, iorT, wi, wo, unit, rnd, inner);
            }
            Facet f = s.lv[d].FacetFor(layer);
            if (f.kind == FacetKind.Diffuse && wi.z > 0 && wo.z > 0)
            {
                double c = wo.z / ffPi;
                return unit ? new SampledSpectrum((Float)c) : f.R * (Float)c;
            }
            return new SampledSpectrum(0f);
        }

        // singular part (specular facets): the Fresnel / reflectance factor
        public static SampledSpectrum FacetEvalSingular(Stack s, int d, double iorI, double iorT, V3 wi, V3 wo, bool unit, int layer)
        {
            if (d + 1 < s.n)
            {
                return new SampledSpectrum(0f);
            }
            Facet f = s.lv[d].FacetFor(layer);
            switch (f.kind)
            {
            case FacetKind.Conductor:
                if (unit)
                {
                    return new SampledSpectrum(1f);
                }
                return FacetFrConductor(f, wi.z, iorI);
            case FacetKind.Dielectric:
            {
                double eta = iorT / iorI;
                SampledSpectrum F = FacetFrDielectric(f, wi.z, iorI, iorT);
                if (f.Filmed() && unit)
                {
                    F = new SampledSpectrum((Float)F.Average());
                }
                return wo.z >= 0 ? F : (new SampledSpectrum(1f) - F) * (Float)(eta * eta);
            }
            case FacetKind.Mirror:
                return unit ? new SampledSpectrum(1f) : f.R;
            default:
                return new SampledSpectrum(0f);
            }
        }

        public static bool FacetSampleFrame(Stack s, int d, double iorI, double iorT, V3 wi, V3 wm, ref SampledSpectrum w, bool unit, Rnd rnd,
                                            out V3 wo, int layer, Polar pol = null, bool outside = true)
        {
            V3 a, b;
            TangentBasis(wm, out a, out b);
            V3 wl = new V3(Dot(wi, a), Dot(wi, b), Dot(wi, wm)), ol;
            Polar inner = (pol != null && d + 1 < s.n) ? pol.Inner(a, b, wm, outside) : null;
            if (!FacetSample(s, d, iorI, iorT, wl, ref w, unit, rnd, out ol, layer, inner))
            {
                wo = default;
                return false;
            }
            wo = ol.x * a + ol.y * b + ol.z * wm;
            if (pol != null)
            {
                pol.Scattered(s, d, iorI, iorT, wi, wo, wm, wl, ol, layer, inner, outside);
            }
            return true;
        }

        private static void Local(V3 a, V3 b, V3 wh, out V3 la, out V3 lb)
        {
            V3 t1, t2;
            Basis(wh, out t1, out t2);
            la = new V3(Dot(a, t1), Dot(a, t2), Dot(a, wh));
            lb = new V3(Dot(b, t1), Dot(b, t2), Dot(b, wh));
        }

        // specular-facet phase function at a single scattering event (NEE), reference: evalPhaseFunctionSingular
        public static SampledSpectrum PhaseSingular(Stack s, int d, double iorI, double iorT, V3 wi, V3 wo, bool wiOutside, bool woOutside,
                                                    bool unit, int layer, Polar pol = null)
        {
            Ndf ndf = s.lv[d].ndf;
            if (pol != null)
            {
                pol.hasPhase = false;
            }
            double etaRatio = iorT / iorI;
            double eta = wiOutside ? etaRatio : 1.0 / etaRatio;
            if (wiOutside == woOutside)
            {
                // reflection
                V3 whr = Normalize(wi + wo);
                V3 ar = wiOutside ? wi : -wi, br = wiOutside ? wo : -wo, hr = wiOutside ? whr : -whr;
                double dw = Dot(ar, hr);
                if (dw <= 0)
                {
                    return new SampledSpectrum(0f);
                }
                double ddr = 0.25 * ndf.DwiLayer(layer, ar, hr) / dw;
                V3 lar, lbr;
                Local(ar, br, hr, out lar, out lbr);
                if (pol != null && d + 1 >= s.n)
                {
                    pol.Phase(s.lv[d].FacetFor(layer), wi, wo, whr, lar.z, eta, true, ddr);
                }
                return (Float)ddr * FacetEvalSingular(s, d, 1.0, eta, lar, lbr, unit, layer);
            }
            // transmission
            V3 wh = -Normalize(wi + eta * wo);
            wh = (wiOutside ? Math.CopySign(1.0, wh.z) : -Math.CopySign(1.0, wh.z)) * wh;
            if (Dot(wh, wi) < 0)
            {
                return new SampledSpectrum(0f);
            }
            V3 a = wiOutside ? wi : -wi, b = wiOutside ? wo : -wo, h = wiOutside ? wh : -wh;
            double den = Dot(a, h) + eta * Dot(b, h);
            if (den == 0)
            {
                return new SampledSpectrum(0f);
            }
            double dd = ndf.DwiLayer(layer, a, h) * Math.Max(0.0, -Dot(b, h)) / (den * den);
            V3 la, lb;
            Local(a, b, h, out la, out lb);
            if (pol != null && d + 1 >= s.n)
            {
                pol.Phase(s.lv[d].FacetFor(layer), wi, wo, wh, la.z, eta, false, dd * eta * eta);
            }
            return (Float)dd * FacetEvalSingular(s, d, 1.0, eta, la, lb, unit, layer);
        }

        // ------------------------------------------------------------------------------------------
        // Closed-form single scattering (d'Eon et al. 2023, Eqs. 12-21), specular facets, single level. Returns
        // a = f1 * |cos wo| for wi.z > 0 (wo in either hemisphere). A layered NDF is a stack of strata (top first); a non-layered
        // NDF is one stratum with the blended NDF.
        //   reflection : a = sum_j T_j (1 - exp(-k_j len_j)) * p_j sigma_j(wi)/(cos_i k_j),
        //                k_j = 1 + Lam_j(wi) + Lam_j(wo)                                (Eq. 15)
        //   refraction : a = sum_j p_j sigma_j(wi)/cos_i * P_j, P_j = incomplete-Beta traversal probability
        //                (Eqs. 20/21 for two strata, here for any number of strata).
        // ------------------------------------------------------------------------------------------
        public static double IncBetaFull(double a, double b, double x)
        {
            // B(x; a, b) = int_0^x t^(a-1) (1-t)^(b-1) dt
            if (x <= 0)
            {
                return 0;
            }
            return IncBeta(a, b, Math.Min(1.0, x)) * Math.Exp(pbrt.LogGamma(a) + pbrt.LogGamma(b) - pbrt.LogGamma(a + b));
        }

        public static bool SingleScatterSupported(Stack s)
        {
            if (s.n != 1)
            {
                return false;
            }
            FacetKind k = s.lv[0].facet.kind;
            return k == FacetKind.Conductor || k == FacetKind.Mirror || k == FacetKind.Dielectric;
        }

        public static SampledSpectrum SingleScatterClosed(Stack s, double iorI, double iorT, V3 wi, V3 wo, bool unit, Polar pol = null)
        {
            Surface sf = s.lv[0];
            Ndf nd = sf.ndf;
            SampledSpectrum res = new SampledSpectrum(0f);
            if (wi.z <= 0 || wo.z == 0)
            {
                return res;
            }
            bool layered = nd.layered && nd.n > 1;
            int nl = layered ? nd.n : 1;
            // (Sig: the cross section of stratum j; Lam: its Lambda, for a direction that points up)
            double Sig(int j, V3 w) { return layered ? nd.c[j].Sigma(w) : nd.Sigma(w); }
            double Lam(int j, V3 w) { return Sig(j, w) / w.z - 1.0; }
            // stratum boundaries in H = exp(z): h[0] = 1 > h[1] > ... > h[nl] = 0
            double* h = stackalloc double[kMaxComps + 1];
            h[0] = 1.0;
            {
                double cum = 0;
                for (int j = 0; j < nl; ++j)
                {
                    cum += layered ? nd.c[j].w : 1.0;
                    h[j + 1] = (j == nl - 1) ? 0.0 : Math.Max(0.0, 1.0 - cum);
                }
            }
            double ci = wi.z;
            bool refl = wo.z > 0;
            if (refl)
            {
                double T = 1.0;
                for (int j = 0; j < nl; ++j)
                {
                    double kj = 1.0 + Lam(j, wi) + Lam(j, wo);
                    double len = (j == nl - 1) ? 1e300 : Math.Log(h[j] / h[j + 1]);
                    double frac = (j == nl - 1) ? 1.0 : -Expm1(-kj * len);
                    SampledSpectrum ps = PhaseSingular(s, 0, iorI, iorT, wi, wo, true, true, unit, layered ? j : -1, pol);
                    double wgt = T * frac * (Sig(j, wi) / ci) / kj;
                    res += ps * (Float)wgt;
                    if (pol != null)
                    {
                        pol.AddPhase(false, wgt);
                    }
                    if (j < nl - 1)
                    {
                        T *= Math.Exp(-kj * len);
                    }
                }
                return res;
            }
            if (sf.facet.kind != FacetKind.Dielectric)
            {
                return res;
            }
            // refraction: ray enters from above, collides in stratum j, refracts, exits through the flipped volume
            V3 wup = new V3(-wo.x, -wo.y, -wo.z);  // points up
            double logAbove = 0.0;                 // log of the transmittance through the strata above stratum j
            for (int j = 0; j < nl; ++j)
            {
                double a = 1.0 + Lam(j, wi), kap = Lam(j, wup);
                double lo = h[j + 1], hi = h[j];
                double Bd = IncBetaFull(a, kap + 1.0, hi) - IncBetaFull(a, kap + 1.0, lo);
                double logc = logAbove - a * Math.Log(hi);
                if (j < nl - 1)
                {
                    logc -= kap * Log1p(-lo);
                }
                for (int k = j + 1; k < nl; ++k)
                {
                    double kk = Lam(k, wup);
                    logc += kk * (Log1p(-h[k]) - Log1p(-h[k + 1]));
                }
                double P = Math.Exp(logc) * Bd;
                SampledSpectrum ps = PhaseSingular(s, 0, iorI, iorT, wi, wo, true, false, unit, layered ? j : -1, pol);
                res += ps * (Float)((Sig(j, wi) / ci) * P);
                if (pol != null)
                {
                    pol.AddPhase(false, (Sig(j, wi) / ci) * P);
                }
                if (j < nl - 1)
                {
                    logAbove += (1.0 + Lam(j, wi)) * (Math.Log(lo) - Math.Log(hi));
                }
            }
            return res;
        }

        public static bool SampleLevel(Stack s, int d, double iorI, double iorT, V3 wi, ref SampledSpectrum w, bool unit, Rnd rnd, out V3 wo,
                                       Polar pol = null)
        {
            Surface sf = s.lv[d];
            wo = default;
            if (pol != null)
            {
                pol.hasChain = false;
            }
            if (wi.z < 0)
            {
                w = new SampledSpectrum(0f);
                return false;
            }
            V3 wr = -wi;
            double hr = 0.0;
            bool outside = true;
            int count = 0;
            while (count < sf.maxWalk + 1)
            {
                V3 wm = default;
                int layer = -1;
                hr = sf.ndf.SampleHeight(wr, hr, rnd, ref wm, !outside, ref layer);
                if (hr >= 0.0)
                {
                    break;
                }
                count++;
                V3 nr;
                if (!FacetSampleFrame(s, d, outside ? iorI : iorT, outside ? iorT : iorI, -wr, wm, ref w, unit, rnd, out nr, layer, pol, outside))
                {
                    w = new SampledSpectrum(0f);
                    return false;
                }
                {
                    double l2 = Dot(nr, nr);
                    if (!(l2 > 0.5 && l2 < 2.0))
                    {
                        // numerically invalid direction
                        w = new SampledSpectrum(0f);
                        return false;
                    }
                    nr = (1.0 / Math.Sqrt(l2)) * nr;  // stop round-off drift accumulating over the walk
                }
                wr = nr;
                if (Dot(wr, wm) < 0.0)
                {
                    outside = !outside;
                    wr = -wr;
                    hr = Math.Log(-Expm1(hr));
                }
                if (Double.IsNaN(hr) || Double.IsNaN(wr.z) || Double.IsInfinity(hr))
                {
                    w = new SampledSpectrum(0f);
                    return false;
                }
            }
            if (hr < 0.0)
            {
                w = new SampledSpectrum(0f);
                return false;
            }
            wo = outside ? wr : -wr;
            return true;
        }

        public static SampledSpectrum EvalLevel(Stack s, int d, double iorI, double iorT, V3 wi, V3 wo, bool unit, Rnd rnd, Polar pol = null)
        {
            Surface sf = s.lv[d];
            SampledSpectrum sum = new SampledSpectrum(0f);
            if (pol != null)
            {
                pol.Begin(wi, wo);
            }
            if (wi.z < 0)
            {
                return sum;
            }
            // first-order (single scattering) term in closed form: replaces the stochastic NEE at the first collision (identical
            // expectation, no variance)
            bool analytic = sf.analyticSingle && d == 0 && s.n == 1 && SingleScatterSupported(s);
            if (analytic && sf.maxWalk >= 1)
            {
                sum = SingleScatterClosed(s, iorI, iorT, wi, wo, unit, pol);
                for (int i = 0; i < pbrt.NSpectrumSamples; ++i)
                {
                    if (!Single.IsFinite(sum[i]))
                    {
                        sum[i] = 0;
                    }
                }
            }
            V3 wr = -wi;
            double hr = 0.0;
            bool outside = true;
            SampledSpectrum weight = new SampledSpectrum(1f);
            int count = 0;
            while (count < sf.maxWalk)
            {
                V3 wm = default;
                int layer = -1;
                hr = sf.ndf.SampleHeight(wr, hr, rnd, ref wm, !outside, ref layer);
                if (hr >= 0.0)
                {
                    break;
                }
                double ii = outside ? iorI : iorT, it = outside ? iorT : iorI;
                // next event estimation
                SampledSpectrum ps = PhaseSingular(s, d, iorI, iorT, outside ? -wr : wr, wo, outside, wo.z > 0, unit, layer, pol);
                V3 woPhase = outside ? wo : -wo;
                V3 a, b;
                TangentBasis(wm, out a, out b);
                V3 wl = new V3(Dot(-wr, a), Dot(-wr, b), Dot(-wr, wm));
                V3 ol = new V3(Dot(woPhase, a), Dot(woPhase, b), Dot(woPhase, wm));
                Polar inner = (pol != null && d + 1 < s.n) ? pol.Inner(a, b, wm, outside) : null;
                SampledSpectrum ph = FacetEval(s, d, ii, it, wl, ol, unit, rnd, layer, inner);
                double hIn = outside ? Math.Log(-Expm1(hr)) : hr;
                double hOut = outside ? hr : Math.Log(-Expm1(hr));
                // hOut lives in the original frame, hIn in the flipped (refracted) frame
                double shSing = (wo.z > 0) ? sf.ndf.G1(wo, hOut, false) : sf.ndf.G1(-wo, hIn, true);
                bool facetReflected = Dot(woPhase, wm) >= 0.0;
                double sh = (facetReflected == outside) ? sf.ndf.G1(wo, hOut, false) : sf.ndf.G1(-wo, hIn, true);
                SampledSpectrum I = weight * (ps * (Float)shSing + ph * (Float)sh);
                bool fin = !(analytic && count == 0);  // first-order term already added analytically
                for (int i = 0; i < pbrt.NSpectrumSamples; ++i)
                {
                    fin = fin && Single.IsFinite(I[i]);
                }
                if (fin)
                {
                    sum += I;
                    if (pol != null)
                    {
                        pol.AddPhase(true, shSing);
                        pol.AddFacet(inner, ph, sh);
                    }
                }
                // next direction
                count++;
                V3 nr;
                if (!FacetSampleFrame(s, d, ii, it, -wr, wm, ref weight, unit, rnd, out nr, layer, pol, outside))
                {
                    return sum;
                }
                wr = nr;
                if (Dot(wr, wm) < 0.0)
                {
                    outside = !outside;
                    wr = -wr;
                    hr = Math.Log(-Expm1(hr));
                }
                if (Double.IsNaN(hr) || Double.IsNaN(wr.z))
                {
                    return new SampledSpectrum(0f);
                }
            }
            return sum;
        }
    }
}
