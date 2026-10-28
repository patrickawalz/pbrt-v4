// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace pbrt
{
    public static unsafe partial class pbrt
    {
        // Sampling Inline Functions
        public static Float BalanceHeuristic(int nf, Float fPdf, int ng, Float gPdf)
        {
            return (nf * fPdf) / (nf * fPdf + ng * gPdf);
        }

        public static Float PowerHeuristic(int nf, Float fPdf, int ng, Float gPdf)
        {
            Float f = nf * fPdf, g = ng * gPdf;
            if (IsInf(Sqr(f)))
                return 1;
            return Sqr(f) / (Sqr(f) + Sqr(g));
        }

        public static int SampleDiscrete(pstd.span<Float> weights, Float u, Float* pmf = null, Float* uRemapped = null)
        {
            // Handle empty _weights_ for discrete sampling
            if (weights.empty())
            {
                if (pmf != null)
                    *pmf = 0;
                return -1;
            }

            // Compute sum of _weights_
            Float sumWeights = 0;
            for (int i = 0; i < (int)weights.size(); ++i)
                sumWeights += weights[i];

            // Compute rescaled $u'$ sample
            Float up = u * sumWeights;
            if (up == sumWeights)
                up = NextFloatDown(up);

            // Find offset in _weights_ corresponding to $u'$
            int offset = 0;
            Float sum = 0;
            while (sum + weights[offset] <= up)
            {
                sum += weights[offset++];
                DCHECK_LT(offset, (int)weights.size());
            }

            // Compute PMF and remapped _u_ value, if necessary
            if (pmf != null)
                *pmf = weights[offset] / sumWeights;
            if (uRemapped != null)
                *uRemapped = std.min((up - sum) / weights[offset], OneMinusEpsilon);

            return offset;
        }

        public static Float LinearPDF(Float x, Float a, Float b)
        {
            DCHECK(a >= 0 && b >= 0);
            if (x < 0 || x > 1)
                return 0;
            return 2 * Lerp(x, a, b) / (a + b);
        }

        public static Float SampleLinear(Float u, Float a, Float b)
        {
            DCHECK(a >= 0 && b >= 0);
            if (u == 0 && a == 0)
                return 0;
            Float x = u * (a + b) / (a + std.sqrt(Lerp(u, Sqr(a), Sqr(b))));
            return std.min(x, OneMinusEpsilon);
        }

        public static Float InvertLinearSample(Float x, Float a, Float b)
        {
            return x * (a * (2 - x) + b * x) / (a + b);
        }

        public static Float BilinearPDF(Point2f p, pstd.span<Float> w)
        {
            DCHECK_EQ(4, (int)w.size());
            if (p.x < 0 || p.x > 1 || p.y < 0 || p.y > 1)
                return 0;
            if (w[0] + w[1] + w[2] + w[3] == 0)
                return 1;
            return 4 *
                   ((1 - p[0]) * (1 - p[1]) * w[0] + p[0] * (1 - p[1]) * w[1] +
                    (1 - p[0]) * p[1] * w[2] + p[0] * p[1] * w[3]) /
                   (w[0] + w[1] + w[2] + w[3]);
        }

        public static Point2f SampleBilinear(Point2f u, pstd.span<Float> w)
        {
            DCHECK_EQ(4, (int)w.size());
            Point2f p = new Point2f();
            // Sample $y$ for bilinear marginal distribution
            p.y = SampleLinear(u[1], w[0] + w[1], w[2] + w[3]);

            // Sample $x$ for bilinear conditional distribution
            p.x = SampleLinear(u[0], Lerp(p.y, w[0], w[2]), Lerp(p.y, w[1], w[3]));

            return p;
        }

        public static Point2f InvertBilinearSample(Point2f p, pstd.span<Float> w)
        {
            return new Point2f(InvertLinearSample(p.x, Lerp(p.y, w[0], w[2]), Lerp(p.y, w[1], w[3])),
                               InvertLinearSample(p.y, w[0] + w[1], w[2] + w[3]));
        }

        public static Float VisibleWavelengthsPDF(Float lambda)
        {
            if (lambda < 360 || lambda > 830)
                return 0;
            return 0.0039398042f / Sqr(std.cosh(0.0072f * (lambda - 538)));
        }

        public static Float SampleVisibleWavelengths(Float u)
        {
            return 538 - 138.888889f * std.atanh(0.85691062f - 1.82750197f * u);
        }

        public static pstd.array3<Float> SampleUniformTriangle(Point2f u)
        {
            Float b0, b1;
            if (u[0] < u[1])
            {
                b0 = u[0] / 2;
                b1 = u[1] - b0;
            }
            else
            {
                b1 = u[1] / 2;
                b0 = u[0] - b1;
            }
            return new pstd.array3<Float>(b0, b1, 1 - b0 - b1);
        }

        public static Point2f InvertUniformTriangleSample(pstd.array3<Float> b)
        {
            if (b[0] > b[1])
            {
                // b0 = u[0] - u[1] / 2, b1 = u[1] / 2
                return new Point2f(b[0] + b[1], 2 * b[1]);
            }
            else
            {
                // b1 = u[1] - u[0] / 2, b0 = u[0] / 2
                return new Point2f(2 * b[0], b[1] + b[0]);
            }
        }

        public static Float SampleTent(Float u, Float r)
        {
            pstd.span<Float> w = new pstd.span<Float>(new Float[] { 0.5f, 0.5f });
            if (SampleDiscrete(w, u, null, &u) == 0)
                return -r + r * SampleLinear(u, 0, 1);
            else
                return r * SampleLinear(u, 1, 0);
        }

        public static Float TentPDF(Float x, Float r)
        {
            if (std.abs(x) >= r)
                return 0;
            return 1 / r - std.abs(x) / Sqr(r);
        }

        public static Float InvertTentSample(Float x, Float r)
        {
            if (x <= 0)
                return (1 - InvertLinearSample(-x / r, 1, 0)) / 2;
            else
                return 0.5f + InvertLinearSample(x / r, 1, 0) / 2;
        }

        public static Float ExponentialPDF(Float x, Float a)
        {
            DCHECK_GT(a, 0);
            return a * std.exp(-a * x);
        }

        public static Float SampleExponential(Float u, Float a)
        {
            DCHECK_GT(a, 0);
            return -std.log(1 - u) / a;
        }

        public static Float InvertExponentialSample(Float x, Float a)
        {
            DCHECK_GT(a, 0);
            return 1 - std.exp(-a * x);
        }

        public static Float NormalPDF(Float x, Float mu = 0, Float sigma = 1)
        {
            return Gaussian(x, mu, sigma);
        }

        public static Float SampleNormal(Float u, Float mu = 0, Float sigma = 1)
        {
            return mu + Sqrt2 * sigma * ErfInv(2 * u - 1);
        }

        public static Float InvertNormalSample(Float x, Float mu = 0, Float sigma = 1)
        {
            return 0.5f * (1 + std.erf((x - mu) / (sigma * Sqrt2)));
        }

        public static Point2f SampleTwoNormal(Point2f u, Float mu = 0, Float sigma = 1)
        {
            Float r2 = -2 * std.log1p(-u[0]); // log(1-u[0]), robustly.
            return new Point2f(mu + sigma * std.sqrt(r2) * std.cos(2 * Pi * u[1]),
                               mu + sigma * std.sqrt(r2) * std.sin(2 * Pi * u[1]));
        }

        public static Float LogisticPDF(Float x, Float s)
        {
            x = std.abs(x);
            return std.exp(-x / s) / (s * Sqr(1 + std.exp(-x / s)));
        }

        public static Float SampleLogistic(Float u, Float s)
        {
            return -s * std.log(1 / u - 1);
        }

        public static Float InvertLogisticSample(Float x, Float s)
        {
            return 1 / (1 + std.exp(-x / s));
        }

        public static Float TrimmedLogisticPDF(Float x, Float s, Float a, Float b)
        {
            if (x < a || x > b)
                return 0;
            Func<Float, Float> P = (Float xx) => InvertLogisticSample(xx, s);
            return Logistic(x, s) / (P(b) - P(a));
        }

        public static Float SampleTrimmedLogistic(Float u, Float s, Float a, Float b)
        {
            DCHECK_LT(a, b);
            Func<Float, Float> P = (Float xx) => InvertLogisticSample(xx, s);
            u = Lerp(u, P(a), P(b));
            Float x = SampleLogistic(u, s);
            DCHECK(!IsNaN(x));
            return Clamp(x, a, b);
        }

        public static Float InvertTrimmedLogisticSample(Float x, Float s, Float a, Float b)
        {
            DCHECK(a <= x && x <= b);
            Func<Float, Float> P = (Float xx) => InvertLogisticSample(xx, s);
            return (P(x) - P(a)) / (P(b) - P(a));
        }

        public static Float SmoothStepPDF(Float x, Float a, Float b)
        {
            if (x < a || x > b)
                return 0;
            DCHECK_LT(a, b);
            return (2 / (b - a)) * SmoothStep(x, a, b);
        }

        public static Float SampleSmoothStep(Float u, Float a, Float b)
        {
            DCHECK_LT(a, b);
            Func<Float, (Float first, Float second)> cdfMinusU = (Float x) =>
            {
                Float t = (x - a) / (b - a);
                Float P = 2 * Pow(3, t) - Pow(4, t);
                Float PDeriv = SmoothStepPDF(x, a, b);
                return (P - u, PDeriv);
            };
            return NewtonBisection(a, b, cdfMinusU);
        }

        public static Float InvertSmoothStepSample(Float x, Float a, Float b)
        {
            Float t = (x - a) / (b - a);
            Func<Float, Float> P = (Float xx) => 2 * Pow(3, t) - Pow(4, t);
            return (P(x) - P(a)) / (P(b) - P(a));
        }

        public static Point2f SampleUniformDiskPolar(Point2f u)
        {
            Float r = std.sqrt(u[0]);
            Float theta = 2 * Pi * u[1];
            return new Point2f(r * std.cos(theta), r * std.sin(theta));
        }

        public static Point2f InvertUniformDiskPolarSample(Point2f p)
        {
            Float phi = std.atan2(p.y, p.x);
            if (phi < 0)
                phi += 2 * Pi;
            return new Point2f(Sqr(p.x) + Sqr(p.y), phi / (2 * Pi));
        }

        public static Point2f SampleUniformDiskConcentric(Point2f u)
        {
            // Map _u_ to $[-1,1]^2$ and handle degeneracy at the origin
            Point2f uOffset = 2 * u - new Vector2f(1, 1);
            if (uOffset.x == 0 && uOffset.y == 0)
                return new Point2f(0, 0);

            // Apply concentric mapping to point
            Float theta, r;
            if (std.abs(uOffset.x) > std.abs(uOffset.y))
            {
                r = uOffset.x;
                theta = PiOver4 * (uOffset.y / uOffset.x);
            }
            else
            {
                r = uOffset.y;
                theta = PiOver2 - PiOver4 * (uOffset.x / uOffset.y);
            }
            return r * new Point2f(std.cos(theta), std.sin(theta));
        }

        public static Point2f InvertUniformDiskConcentricSample(Point2f p)
        {
            Float theta = std.atan2(p.y, p.x);  // -pi -> pi
            Float r = std.sqrt(Sqr(p.x) + Sqr(p.y));

            Point2f uo = new Point2f();
            // TODO: can we make this less branchy?
            if (std.abs(theta) < PiOver4 || std.abs(theta) > 3 * PiOver4)
            {
                uo.x = r = pstd.copysign(r, p.x);
                if (p.x < 0)
                {
                    if (p.y < 0)
                        uo.y = (Pi + theta) * r / PiOver4;
                    else
                        uo.y = (theta - Pi) * r / PiOver4;
                }
                else
                {
                    uo.y = (theta * r) / PiOver4;
                }
            }
            else
            {
                uo.y = r = pstd.copysign(r, p.y);
                if (p.y < 0)
                    uo.x = -(PiOver2 + theta) * r / PiOver4;
                else
                    uo.x = (PiOver2 - theta) * r / PiOver4;
            }

            return new Point2f((uo.x + 1) / 2, (uo.y + 1) / 2);
        }

        public static Vector3f SampleUniformHemisphere(Point2f u)
        {
            Float z = u[0];
            Float r = SafeSqrt(1 - Sqr(z));
            Float phi = 2 * Pi * u[1];
            return new Vector3f(r * std.cos(phi), r * std.sin(phi), z);
        }

        public static Float UniformHemispherePDF()
        {
            return Inv2Pi;
        }

        public static Point2f InvertUniformHemisphereSample(Vector3f w)
        {
            Float phi = std.atan2(w.y, w.x);
            if (phi < 0)
                phi += 2 * Pi;
            return new Point2f(w.z, phi / (2 * Pi));
        }

        public static Vector3f SampleUniformSphere(Point2f u)
        {
            Float z = 1 - 2 * u[0];
            Float r = SafeSqrt(1 - Sqr(z));
            Float phi = 2 * Pi * u[1];
            return new Vector3f(r * std.cos(phi), r * std.sin(phi), z);
        }

        public static Float UniformSpherePDF()
        {
            return Inv4Pi;
        }

        public static Point2f InvertUniformSphereSample(Vector3f w)
        {
            Float phi = std.atan2(w.y, w.x);
            if (phi < 0)
                phi += 2 * Pi;
            return new Point2f((1 - w.z) / 2, phi / (2 * Pi));
        }

        public static Vector3f SampleCosineHemisphere(Point2f u)
        {
            Point2f d = SampleUniformDiskConcentric(u);
            Float z = SafeSqrt(1 - Sqr(d.x) - Sqr(d.y));
            return new Vector3f(d.x, d.y, z);
        }

        public static Float CosineHemispherePDF(Float cosTheta)
        {
            return cosTheta * InvPi;
        }

        public static Point2f InvertCosineHemisphereSample(Vector3f w)
        {
            return InvertUniformDiskConcentricSample(new Point2f(w.x, w.y));
        }

        public static Float UniformConePDF(Float cosThetaMax)
        {
            return 1 / (2 * Pi * (1 - cosThetaMax));
        }

        public static Vector3f SampleUniformCone(Point2f u, Float cosThetaMax)
        {
            Float cosTheta = (1 - u[0]) + u[0] * cosThetaMax;
            Float sinTheta = SafeSqrt(1 - Sqr(cosTheta));
            Float phi = u[1] * 2 * Pi;
            return SphericalDirection(sinTheta, cosTheta, phi);
        }

        public static Point2f InvertUniformConeSample(Vector3f w, Float cosThetaMax)
        {
            Float cosTheta = w.z;
            Float phi = SphericalPhi(w);
            return new Point2f((cosTheta - 1) / (cosThetaMax - 1), phi / (2 * Pi));
        }

        // Sample from e^(-c x), x from 0 to xMax
        public static Float SampleTrimmedExponential(Float u, Float c, Float xMax)
        {
            return std.log(1 - u * (1 - std.exp(-c * xMax))) / -c;
        }

        public static Float TrimmedExponentialPDF(Float x, Float c, Float xMax)
        {
            if (x < 0 || x > xMax)
                return 0;
            return c / (1 - std.exp(-c * xMax)) * std.exp(-c * x);
        }

        public static Float InvertTrimmedExponentialSample(Float x, Float c, Float xMax)
        {
            DCHECK(x >= 0 && x <= xMax);
            return (1 - std.exp(-c * x)) / (1 - std.exp(-c * xMax));
        }

        public static Vector3f SampleUniformHemisphereConcentric(Point2f u)
        {
            // Map uniform random numbers to $[-1,1]^2$
            Point2f uOffset = 2.0f * u - new Vector2f(1, 1);

            // Handle degeneracy at the origin
            if (uOffset.x == 0 && uOffset.y == 0)
                return new Vector3f(0, 0, 1);

            // Apply concentric mapping to point
            Float theta, r;
            if (std.abs(uOffset.x) > std.abs(uOffset.y))
            {
                r = uOffset.x;
                theta = PiOver4 * (uOffset.y / uOffset.x);
            }
            else
            {
                r = uOffset.y;
                theta = PiOver2 - PiOver4 * (uOffset.x / uOffset.y);
            }

            return new Vector3f(std.cos(theta) * r * std.sqrt(2 - r * r),
                                std.sin(theta) * r * std.sqrt(2 - r * r), 1 - r * r);
        }

        // Sampling Function Definitions
        public static pstd.array3<Float> SampleSphericalTriangle(pstd.array3<Point3f> v, Point3f p, Point2f u, Float* pdf = null)
        {
            if (pdf != null)
                *pdf = 0;
            // Compute vectors _a_, _b_, and _c_ to spherical triangle vertices
            Vector3f a = (v[0] - p), b = (v[1] - p), c = (v[2] - p);
            CHECK_GT(LengthSquared(a), 0);
            CHECK_GT(LengthSquared(b), 0);
            CHECK_GT(LengthSquared(c), 0);
            a = Normalize(a);
            b = Normalize(b);
            c = Normalize(c);

            // Compute normalized cross products of all direction pairs
            Vector3f n_ab = Cross(a, b), n_bc = Cross(b, c), n_ca = Cross(c, a);
            if (LengthSquared(n_ab) == 0 || LengthSquared(n_bc) == 0 || LengthSquared(n_ca) == 0)
                return default;
            n_ab = Normalize(n_ab);
            n_bc = Normalize(n_bc);
            n_ca = Normalize(n_ca);

            // Find angles $\alpha$, $\beta$, and $\gamma$ at spherical triangle vertices
            Float alpha = AngleBetween(n_ab, -n_ca);
            Float beta = AngleBetween(n_bc, -n_ab);
            Float gamma = AngleBetween(n_ca, -n_bc);

            // Uniformly sample triangle area $A$ to compute $A'$
            Float A_pi = alpha + beta + gamma;
            Float Ap_pi = Lerp(u[0], Pi, A_pi);
            if (pdf != null)
            {
                Float A = A_pi - Pi;
                *pdf = (A <= 0) ? 0 : 1 / A;
            }

            // Find $\cos\beta'$ for point along _b_ for sampled area
            Float cosAlpha = std.cos(alpha), sinAlpha = std.sin(alpha);
            Float sinPhi = std.sin(Ap_pi) * cosAlpha - std.cos(Ap_pi) * sinAlpha;
            Float cosPhi = std.cos(Ap_pi) * cosAlpha + std.sin(Ap_pi) * sinAlpha;
            Float k1 = cosPhi + cosAlpha;
            Float k2 = sinPhi - sinAlpha * Dot(a, b) /* cos c */;
            Float cosBp = (k2 + (DifferenceOfProducts(k2, cosPhi, k1, sinPhi)) * cosAlpha) /
                          ((SumOfProducts(k2, sinPhi, k1, cosPhi)) * sinAlpha);
            // Happens if the triangle basically covers the entire hemisphere.
            // We currently depend on calling code to detect this case, which
            // is sort of ugly/unfortunate.
            CHECK(!IsNaN(cosBp));
            cosBp = Clamp(cosBp, -1, 1);

            // Sample $c'$ along the arc between $a$ and $c$
            Float sinBp = SafeSqrt(1 - Sqr(cosBp));
            Vector3f cp = cosBp * a + sinBp * Normalize(GramSchmidt(c, a));

            // Compute sampled spherical triangle direction and return barycentrics
            Float cosTheta = 1 - u[1] * (1 - Dot(cp, b));
            Float sinTheta = SafeSqrt(1 - Sqr(cosTheta));
            Vector3f w = cosTheta * b + sinTheta * Normalize(GramSchmidt(cp, b));
            // Find barycentric coordinates for sampled direction _w_
            Vector3f e1 = v[1] - v[0], e2 = v[2] - v[0];
            Vector3f s1 = Cross(w, e2);
            Float divisor = Dot(s1, e1);
            CHECK_RARE(1e-6, divisor == 0);
            if (divisor == 0)
            {
                // This happens with triangles that cover (nearly) the whole
                // hemisphere.
                return new pstd.array3<Float>(1.0f / 3.0f, 1.0f / 3.0f, 1.0f / 3.0f);
            }
            Float invDivisor = 1 / divisor;
            Vector3f s = p - v[0];
            Float b1 = Dot(s, s1) * invDivisor;
            Float b2 = Dot(w, Cross(s, e1)) * invDivisor;

            // Return clamped barycentrics for sampled direction
            b1 = Clamp(b1, 0, 1);
            b2 = Clamp(b2, 0, 1);
            if (b1 + b2 > 1)
            {
                b1 /= b1 + b2;
                b2 /= b1 + b2;
            }
            return new pstd.array3<Float>((Float)(1 - b1 - b2), (Float)b1, (Float)b2);
        }

        // Via Jim Arvo's SphTri.C
        public static Point2f InvertSphericalTriangleSample(pstd.array3<Point3f> v, Point3f p, Vector3f w)
        {
            // Compute vectors _a_, _b_, and _c_ to spherical triangle vertices
            Vector3f a = (v[0] - p), b = (v[1] - p), c = (v[2] - p);
            CHECK_GT(LengthSquared(a), 0);
            CHECK_GT(LengthSquared(b), 0);
            CHECK_GT(LengthSquared(c), 0);
            a = Normalize(a);
            b = Normalize(b);
            c = Normalize(c);

            // Compute normalized cross products of all direction pairs
            Vector3f n_ab = Cross(a, b), n_bc = Cross(b, c), n_ca = Cross(c, a);
            if (LengthSquared(n_ab) == 0 || LengthSquared(n_bc) == 0 || LengthSquared(n_ca) == 0)
                return default;
            n_ab = Normalize(n_ab);
            n_bc = Normalize(n_bc);
            n_ca = Normalize(n_ca);

            // Find angles $\alpha$, $\beta$, and $\gamma$ at spherical triangle vertices
            Float alpha = AngleBetween(n_ab, -n_ca);
            Float beta = AngleBetween(n_bc, -n_ab);
            Float gamma = AngleBetween(n_ca, -n_bc);

            // Find vertex $\VEC{c'}$ along $\VEC{a}\VEC{c}$ arc for $\w{}$
            Vector3f cp = Normalize(Cross(Cross(b, w), Cross(c, a)));
            if (Dot(cp, a + c) < 0)
                cp = -cp;

            // Invert uniform area sampling to find _u0_
            Float u0;
            if (Dot(a, cp) > 0.99999847691f /* 0.1 degrees */)
                u0 = 0;
            else
            {
                // Compute area $A'$ of subtriangle
                Vector3f n_cpb = Cross(cp, b), n_acp = Cross(a, cp);
                CHECK_RARE(1e-5, LengthSquared(n_cpb) == 0 || LengthSquared(n_acp) == 0);
                if (LengthSquared(n_cpb) == 0 || LengthSquared(n_acp) == 0)
                    return new Point2f(0.5f, 0.5f);
                n_cpb = Normalize(n_cpb);
                n_acp = Normalize(n_acp);
                Float Ap = alpha + AngleBetween(n_ab, n_cpb) + AngleBetween(n_acp, -n_cpb) - Pi;

                // Compute sample _u0_ that gives the area $A'$
                Float A = alpha + beta + gamma - Pi;
                u0 = Ap / A;
            }

            // Invert arc sampling to find _u1_ and return result
            Float u1 = (1 - Dot(w, b)) / (1 - Dot(cp, b));
            return new Point2f(Clamp(u0, 0, 1), Clamp(u1, 0, 1));
        }

        public static Point3f SampleSphericalRectangle(Point3f pRef, Point3f s, Vector3f ex, Vector3f ey, Point2f u, Float* pdf = null)
        {
            // Compute local reference frame and transform rectangle coordinates
            Float exl = Length(ex), eyl = Length(ey);
            Frame R = Frame.FromXY(ex / exl, ey / eyl);
            Vector3f dLocal = R.ToLocal(s - pRef);
            Float z0 = dLocal.z;

            // flip 'z' to make it point against 'Q'
            if (z0 > 0)
            {
                R.z = -R.z;
                z0 *= -1;
            }
            Float x0 = dLocal.x, y0 = dLocal.y;
            Float x1 = x0 + exl, y1 = y0 + eyl;

            // Find plane normals to rectangle edges and compute internal angles
            Vector3f v00 = new Vector3f(x0, y0, z0), v01 = new Vector3f(x0, y1, z0);
            Vector3f v10 = new Vector3f(x1, y0, z0), v11 = new Vector3f(x1, y1, z0);
            Vector3f n0 = Normalize(Cross(v00, v10)), n1 = Normalize(Cross(v10, v11));
            Vector3f n2 = Normalize(Cross(v11, v01)), n3 = Normalize(Cross(v01, v00));
            Float g0 = AngleBetween(-n0, n1), g1 = AngleBetween(-n1, n2);
            Float g2 = AngleBetween(-n2, n3), g3 = AngleBetween(-n3, n0);

            // Compute spherical rectangle solid angle and PDF
            Float solidAngle = g0 + g1 + g2 + g3 - 2 * Pi;
            CHECK_RARE(1e-5, solidAngle <= 0);
            if (solidAngle <= 0)
            {
                if (pdf != null)
                    *pdf = 0;
                return s + u[0] * ex + u[1] * ey;
            }
            if (pdf != null)
                *pdf = std.max<Float>(0, 1 / solidAngle);
            if (solidAngle < 1e-3)
                return s + u[0] * ex + u[1] * ey;

            // Sample _cu_ for spherical rectangle sample
            Float b0 = n0.z, b1 = n2.z;
            Float au = u[0] * (g0 + g1 - 2 * Pi) + (u[0] - 1) * (g2 + g3);
            Float fu = (std.cos(au) * b0 - b1) / std.sin(au);
            Float cu = pstd.copysign(1 / std.sqrt(Sqr(fu) + Sqr(b0)), fu);
            cu = Clamp(cu, -OneMinusEpsilon, OneMinusEpsilon);  // avoid NaNs

            // Find _xu_ along $x$ edge for spherical rectangle sample
            Float xu = -(cu * z0) / SafeSqrt(1 - Sqr(cu));
            xu = Clamp(xu, x0, x1);

            // Find _xv_ along $y$ edge for spherical rectangle sample
            Float dd = std.sqrt(Sqr(xu) + Sqr(z0));
            Float h0 = y0 / std.sqrt(Sqr(dd) + Sqr(y0));
            Float h1 = y1 / std.sqrt(Sqr(dd) + Sqr(y1));
            Float hv = h0 + u[1] * (h1 - h0), hvsq = Sqr(hv);
            Float yv = (hvsq < 1 - 1e-6f) ? (hv * dd) / std.sqrt(1 - hvsq) : y1;

            // Return spherical triangle sample in original coordinate system
            return pRef + R.FromLocal(new Vector3f(xu, yv, z0));
        }

        public static Point2f InvertSphericalRectangleSample(Point3f pRef, Point3f s, Vector3f ex, Vector3f ey, Point3f pRect)
        {
            // TODO: Delete anything unused in the below...

            // SphQuadInit()
            // local reference system 'R'
            Float exl = Length(ex), eyl = Length(ey);
            Frame R = Frame.FromXY(ex / exl, ey / eyl);

            // compute rectangle coords in local reference system
            Vector3f d = s - pRef;
            Vector3f dLocal = R.ToLocal(d);
            Float z0 = dLocal.z;

            // flip 'z' to make it point against 'Q'
            if (z0 > 0)
            {
                R.z = -R.z;
                z0 *= -1;
            }
            Float z0sq = Sqr(z0);
            Float x0 = dLocal.x;
            Float y0 = dLocal.y;
            Float x1 = x0 + exl;
            Float y1 = y0 + eyl;
            Float y0sq = Sqr(y0), y1sq = Sqr(y1);

            // create vectors to four vertices
            Vector3f v00 = new Vector3f(x0, y0, z0), v01 = new Vector3f(x0, y1, z0);
            Vector3f v10 = new Vector3f(x1, y0, z0), v11 = new Vector3f(x1, y1, z0);

            // compute normals to edges
            Vector3f n0 = Normalize(Cross(v00, v10));
            Vector3f n1 = Normalize(Cross(v10, v11));
            Vector3f n2 = Normalize(Cross(v11, v01));
            Vector3f n3 = Normalize(Cross(v01, v00));

            // compute internal angles (gamma_i)
            Float g0 = AngleBetween(-n0, n1);
            Float g1 = AngleBetween(-n1, n2);
            Float g2 = AngleBetween(-n2, n3);
            Float g3 = AngleBetween(-n3, n0);

            // compute predefined constants
            Float b0 = n0.z, b1 = n2.z, b0sq = Sqr(b0), b1sq = Sqr(b1);

            // compute solid angle from internal angles
            Float solidAngle = (Float)((double)g0 + (double)g1 + (double)g2 + (double)g3 - 2.0 * Pi);

            // TODO: this (rarely) goes differently than sample. figure out why...
            if (solidAngle < 1e-3)
            {
                Vector3f pq = pRect - s;
                return new Point2f(Dot(pq, ex) / LengthSquared(ex), Dot(pq, ey) / LengthSquared(ey));
            }

            Vector3f v = R.ToLocal(pRect - pRef);
            Float xu = v.x, yv = v.y;

            xu = Clamp(xu, x0, x1);  // avoid Infs
            if (xu == 0)
                xu = 1e-10f;

            // Doing all this in double actually makes things slightly worse???!?
            // Float fusq = (1 - b0sq * Sqr(cu)) / Sqr(cu);
            // Float fusq = 1 / Sqr(cu) - b0sq;  // more stable
            Float invcusq = 1 + z0sq / Sqr(xu);
            Float fusq = invcusq - b0sq;  // the winner so far
            Float fu = pstd.copysign(std.sqrt(fusq), xu);
            // Note, though have 1 + z^2/x^2 - b0^2, which isn't great if b0 \approx 1
            // double fusq = 1. - Sqr(double(b0)) + Sqr(double(z0) / double(xu));  //
            // this is worse?? double fu = pstd::copysign(std::sqrt(fusq), cu);
            CHECK_RARE(1e-6, fu == 0);

            // State of the floating point world: in the bad cases, about half the
            // error seems to come from inaccuracy in fu and half comes from
            // inaccuracy in sqrt/au.
            //
            // For fu, the main issue comes adding a small value to 1+ in invcusq
            // and then having b0sq be close to one, so having catastrophic
            // cancellation affect fusq. Approximating it as z0sq / Sqr(xu) when
            // b0sq is close to one doesn't help, however..
            //
            // For au, DifferenceOfProducts doesn't seem to help with the two
            // factors. Furthermore, while it would be nice to think about this
            // like atan(y/x) and then rewrite/simplify y/x, we need to do so in a
            // way that doesn't flip the sign of x and y, which would be fine if we
            // were computing y/x, but messes up atan2's quadrant-determinations...

            Float sqrt = SafeSqrt(DifferenceOfProducts(b0, b0, b1, b1) + fusq);
            // No benefit to difference of products here...
            Float au = std.atan2(-(b1 * fu) - pstd.copysign(b0 * sqrt, fu * b0),
                                 b0 * b1 - sqrt * std.abs(fu));
            if (au > 0)
                au -= 2 * Pi;

            if (fu == 0)
                au = Pi;
            Float u0 = (au + g2 + g3) / solidAngle;

            Float ddsq = Sqr(xu) + z0sq;
            Float dd = std.sqrt(ddsq);
            Float h0 = y0 / std.sqrt(ddsq + y0sq);
            Float h1 = y1 / std.sqrt(ddsq + y1sq);
            Float yvsq = Sqr(yv);

            Float u1_0 = (DifferenceOfProducts(h0, h0, h0, h1) -
                          std.abs(h0 - h1) * std.sqrt(yvsq * (ddsq + yvsq)) / (ddsq + yvsq)) /
                         Sqr(h0 - h1);
            Float u1_1 = (DifferenceOfProducts(h0, h0, h0, h1) +
                          std.abs(h0 - h1) * std.sqrt(yvsq * (ddsq + yvsq)) / (ddsq + yvsq)) /
                         Sqr(h0 - h1);

            // TODO: yuck is there a better way to figure out which is the right
            // solution?
            Float hv_0 = Lerp(u1_0, h0, h1), hv_1 = Lerp(u1_1, h0, h1);
            Float hvsq_0 = Sqr(hv_0), hvsq_1 = Sqr(hv_1);
            Float yz_0 = (hv_0 * dd) / std.sqrt(1 - hvsq_0);
            Float yz_1 = (hv_1 * dd) / std.sqrt(1 - hvsq_1);

            Point2f u = (std.abs(yz_0 - yv) < std.abs(yz_1 - yv))
                            ? new Point2f(Clamp(u0, 0, 1), u1_0)
                            : new Point2f(Clamp(u0, 0, 1), u1_1);

            return u;
        }

        public static Vector3f SampleHenyeyGreenstein(Vector3f wo, Float g, Point2f u, Float* pdf = null)
        {
            // When g \approx -1 and u[0] \approx 0 or with g \approx 1 and u[0]
            // \approx 1, the computation of cosTheta below is unstable and can
            // give, leading to NaNs. For now we limit g to the range where it is
            // still ok; it would be nice to come up with a numerically robust
            // rewrite of the computation instead, but with |g| \approx 1, the
            // sampling distribution has a very sharp turn to deal with.
            g = Clamp(g, (Float)(-.99), (Float).99);

            // Compute $\cos\theta$ for Henyey--Greenstein sample
            Float cosTheta;
            if (std.abs(g) < 1e-3f)
                cosTheta = 1 - 2 * u[0];
            else
                cosTheta =
                    -1 / (2 * g) * (1 + Sqr(g) - Sqr((1 - Sqr(g)) / (1 + g - 2 * g * u[0])));

            // Compute direction _wi_ for Henyey--Greenstein sample
            Float sinTheta = SafeSqrt(1 - Sqr(cosTheta));
            Float phi = 2 * Pi * u[1];
            Frame wFrame = Frame.FromZ(wo);
            Vector3f wi = wFrame.FromLocal(SphericalDirection(sinTheta, cosTheta, phi));

            if (pdf != null)
                *pdf = HenyeyGreenstein(cosTheta, g);
            return wi;
        }

        public static Point2f RejectionSampleDisk(ref RNG rng)
        {
            Point2f p = new Point2f();
            do
            {
                p.x = 1 - 2 * rng.Uniform<Float>();
                p.y = 1 - 2 * rng.Uniform<Float>();
            } while (Sqr(p.x) + Sqr(p.y) > 1);
            return p;
        }

        public static Float SampleCatmullRom(pstd.span<Float> nodes, pstd.span<Float> f, pstd.span<Float> F, Float u, Float* fval = null, Float* pdf = null)
        {
            CHECK_EQ(nodes.size(), f.size());
            CHECK_EQ(f.size(), F.size());
            // Map _u_ to a spline interval by inverting _F_
            u *= F.back();
            int i = (int)FindInterval(F.size(), F, (pstd.span<Float> FF, int ii) => FF[ii] <= u);

            // Look up $x_i$ and function values of spline segment _i_
            Float x0 = nodes[i], x1 = nodes[i + 1];
            Float f0 = f[i], f1 = f[i + 1];
            Float width = x1 - x0;

            // Approximate derivatives using finite differences
            Float d0 = (i > 0) ? width * (f1 - f[i - 1]) / (x1 - nodes[i - 1]) : (f1 - f0);
            Float d1 = ((ulong)(i + 2) < nodes.size()) ? width * (f[i + 2] - f0) / (nodes[i + 2] - x0)
                                                       : (f1 - f0);

            // Rescale _u_ for continuous spline sampling step
            u = (u - F[i]) / width;

            // Invert definite integral over spline segment
            Float Fhat = 0, fhat = 0;
            Func<Float, (Float first, Float second)> eval = (Float t) =>
            {
                Fhat = EvaluatePolynomial(t, 0, f0, 0.5f * d0,
                                          (1.0f / 3.0f) * (-2 * d0 - d1) + f1 - f0,
                                          0.25f * (d0 + d1) + 0.5f * (f0 - f1));
                fhat = EvaluatePolynomial(t, f0, d0, -2 * d0 - d1 + 3 * (f1 - f0),
                                          d0 + d1 + 2 * (f0 - f1));
                return (Fhat - u, fhat);
            };
            Float tt = NewtonBisection(0, 1, eval);

            if (fval != null)
                *fval = fhat;
            if (pdf != null)
                *pdf = fhat / F.back();
            return x0 + width * tt;
        }

        public static Float SampleCatmullRom2D(pstd.span<Float> nodes1, pstd.span<Float> nodes2, pstd.span<Float> values, pstd.span<Float> cdf, Float alpha, Float u, Float* fval = null, Float* pdf = null)
        {
            // Determine offset and coefficients for the _alpha_ parameter
            int offsetOut;
            Float[] w = new Float[4];
            pstd.span<Float> weightsSpan = new pstd.span<Float>(w);
            if (!CatmullRomWeights(nodes1, alpha, &offsetOut, weightsSpan))
                return 0;
            int offset = offsetOut;
            int nNodes2 = (int)nodes2.size();

            // Define a lambda function to interpolate table entries
            Func<pstd.span<Float>, int, Float> interpolate = (pstd.span<Float> array, int idx) =>
            {
                Float v = 0;
                for (int i = 0; i < 4; ++i)
                    if (w[i] != 0)
                        v += array[(offset + i) * nNodes2 + idx] * w[i];
                return v;
            };

            // Map _u_ to a spline interval by inverting the interpolated _cdf_
            Float maximum = interpolate(cdf, (int)nodes2.size() - 1);
            u *= maximum;
            int idx0 = (int)FindInterval(nodes2.size(), cdf, (pstd.span<Float> c, int i) => interpolate(c, i) <= u);

            // Look up node positions and interpolated function values
            Float f0 = interpolate(values, idx0), f1 = interpolate(values, idx0 + 1);
            Float x0 = nodes2[idx0], x1 = nodes2[idx0 + 1];
            Float width = x1 - x0;
            Float d0, d1;

            // Rescale _u_ using the interpolated _cdf_
            u = (u - interpolate(cdf, idx0)) / width;

            // Approximate derivatives using finite differences of the interpolant
            if (idx0 > 0)
                d0 = width * (f1 - interpolate(values, idx0 - 1)) / (x1 - nodes2[idx0 - 1]);
            else
                d0 = f1 - f0;
            if ((ulong)(idx0 + 2) < nodes2.size())
                d1 = width * (interpolate(values, idx0 + 2) - f0) / (nodes2[idx0 + 2] - x0);
            else
                d1 = f1 - f0;

            // Invert definite integral over spline segment
            Float Fhat = 0, fhat = 0;
            Func<Float, (Float first, Float second)> eval = (Float t) =>
            {
                Fhat = EvaluatePolynomial(t, 0, f0, 0.5f * d0,
                                          (1.0f / 3.0f) * (-2 * d0 - d1) + f1 - f0,
                                          0.25f * (d0 + d1) + 0.5f * (f0 - f1));
                fhat = EvaluatePolynomial(t, f0, d0, -2 * d0 - d1 + 3 * (f1 - f0),
                                          d0 + d1 + 2 * (f0 - f1));
                return (Fhat - u, fhat);
            };
            Float tt = NewtonBisection(0, 1, eval);

            if (fval != null)
                *fval = fhat;
            if (pdf != null)
                *pdf = fhat / maximum;
            return x0 + width * tt;
        }

        public static pstd.vector<Float> Sample1DFunction(Func<Float, Float> f, int nSteps, int nSamples, Float min = 0, Float max = 1, Allocator alloc = null)
        {
            pstd.vector<Float> values = new pstd.vector<Float>((ulong)nSteps, (Float)0);
            for (int i = 0; i < nSteps; ++i)
            {
                double accum = 0;
                // One extra so that we sample at the very start and the very end.
                for (int j = 0; j < nSamples + 1; ++j)
                {
                    Float delta = (Float)j / nSamples;
                    Float v = Lerp((i + delta) / (Float)nSteps, min, max);
                    Float fv = std.abs(f(v));
                    accum = std.max<double>(accum, fv);
                }
                // There's actually no need for the divide by nSamples, since
                // these are normalzed into a PDF anyway.
                values[i] = (Float)accum;
            }
            return values;
        }

        public static Array2D<Float> Sample2DFunction(Func<Float, Float, Float> f, int nu, int nv, int nSamples, Bounds2f domain = default, Allocator alloc = null)
        {
            if (domain.pMin == domain.pMax)
                domain = new Bounds2f(new Point2f(0, 0), new Point2f(1, 1));
            List<Point2f> samples = new List<Point2f>(nSamples);
            for (int i = 0; i < nSamples; ++i)
                samples.Add(new Point2f(RadicalInverse(0, (uint64_t)i), RadicalInverse(1, (uint64_t)i)));
            // Check the corners, too.
            samples.Add(new Point2f(0, 1));
            samples.Add(new Point2f(1, 0));
            samples.Add(new Point2f(1, 1));

            Array2D<Float> values = new Array2D<Float>(nu, nv, alloc);
            for (int v = 0; v < nv; ++v)
            {
                for (int u = 0; u < nu; ++u)
                {
                    double accum = 0;
                    for (int i = 0; i < samples.Count; ++i)
                    {
                        Point2f p = domain.Lerp(
                            new Point2f((u + samples[i][0]) / nu, (v + samples[i][1]) / nv));
                        Float fuv = std.abs(f(p.x, p.y));
                        accum = std.max<double>(accum, fuv);
                    }
                    // There's actually no need for the divide by nSamples, since
                    // these are normalzed into a PDF anyway.
                    values[u, v] = (Float)accum;
                }
            }

            return values;
        }
    }

    // VarianceEstimator Definition (template <typename Float> class VarianceEstimator)
    public unsafe struct VarianceEstimator<TF> where TF : unmanaged, System.Numerics.IFloatingPointIeee754<TF>
    {
        // VarianceEstimator Public Methods
        public void Add(TF x)
        {
            ++n;
            TF delta = x - mean;
            mean += delta / TF.CreateChecked(n);
            TF delta2 = x - mean;
            S += delta * delta2;
        }

        public TF Mean() { return mean; }
        public TF Variance() { return (n > 1) ? S / TF.CreateChecked(n - 1) : TF.Zero; }
        public int64_t Count() { return n; }
        public TF RelativeVariance()
        {
            return (n < 1 || mean == TF.Zero) ? TF.Zero : Variance() / Mean();
        }

        public void Merge(VarianceEstimator<TF> ve)
        {
            if (ve.n == 0)
                return;
            TF dm = ve.mean - mean;
            S = S + ve.S + (dm * dm) * TF.CreateChecked(n) * TF.CreateChecked(ve.n) / TF.CreateChecked(n + ve.n);
            mean = (TF.CreateChecked(n) * mean + TF.CreateChecked(ve.n) * ve.mean) / TF.CreateChecked(n + ve.n);
            n += ve.n;
        }

        // VarianceEstimator Private Members
        private TF mean, S;
        private int64_t n;
    }

    // WeightedReservoirSampler Definition
    public unsafe struct WeightedReservoirSampler<T>
    {
        // WeightedReservoirSampler Public Methods
        public WeightedReservoirSampler(uint64_t rngSeed)
        {
            rng = new RNG(rngSeed);
            weightSum = 0;
            reservoirWeight = 0;
            reservoir = default;
        }

        public void Seed(uint64_t seed) { rng.SetSequence(seed); }

        public bool Add(T sample, Float weight)
        {
            weightSum += weight;
            // Randomly add _sample_ to reservoir
            Float p = weight / weightSum;
            if (rng.Uniform<Float>() < p)
            {
                reservoir = sample;
                reservoirWeight = weight;
                return true;
            }
            DCHECK_LT(weightSum, 1e80);
            return false;
        }

        public bool Add(Func<T> func, Float weight)
        {
            // Process weighted reservoir sample via callback
            weightSum += weight;
            Float p = weight / weightSum;
            if (rng.Uniform<Float>() < p)
            {
                reservoir = func();
                reservoirWeight = weight;
                return true;
            }
            DCHECK_LT(weightSum, 1e80);
            return false;
        }

        public void Copy(WeightedReservoirSampler<T> wrs)
        {
            weightSum = wrs.weightSum;
            reservoir = wrs.reservoir;
            reservoirWeight = wrs.reservoirWeight;
        }

        public int HasSample() { return weightSum > 0 ? 1 : 0; }
        public T GetSample() { return reservoir; }
        public Float SampleProbability() { return reservoirWeight / weightSum; }
        public Float WeightSum() { return weightSum; }

        public void Reset() { reservoirWeight = weightSum = 0; }

        public void Merge(WeightedReservoirSampler<T> wrs)
        {
            DCHECK_LE(weightSum + wrs.WeightSum(), 1e80);
            if (wrs.HasSample() != 0 && Add(wrs.reservoir, wrs.weightSum))
                reservoirWeight = wrs.reservoirWeight;
        }

        public override string ToString()
        {
            return $"[ WeightedReservoirSampler rng: {rng} weightSum: {weightSum} reservoir: {reservoir} reservoirWeight: {reservoirWeight} ]";
        }

        // WeightedReservoirSampler Private Members
        private RNG rng;
        private Float weightSum;
        private Float reservoirWeight;
        private T reservoir;
    }

    // PiecewiseConstant1D Definition
    public unsafe class PiecewiseConstant1D
    {
        // PiecewiseConstant1D Public Methods
        public size_t BytesUsed()
        {
            return (func.capacity() + cdf.capacity()) * sizeof(Float);
        }

        public override string ToString()
        {
            return $"[ PiecewiseConstant1D min: {min} max: {max} funcInt: {funcInt} ]";
        }

        public PiecewiseConstant1D() { func = new pstd.vector<Float>(); cdf = new pstd.vector<Float>(); }
        public PiecewiseConstant1D(Allocator alloc) : this() { }
        public PiecewiseConstant1D(pstd.span<Float> f, Allocator alloc = null)
            : this(f, 0.0f, 1.0f, alloc) { }

        public PiecewiseConstant1D(pstd.span<Float> f, Float min, Float max, Allocator alloc = null)
        {
            func = new pstd.vector<Float>(f.ToArray());
            cdf = new pstd.vector<Float>(f.size() + 1);
            this.min = min;
            this.max = max;
            CHECK_GT(max, min);
            // Take absolute value of _func_
            for (ulong i = 0; i < func.size(); ++i)
                func[i] = std.abs(func[i]);

            // Compute integral of step function at $x_i$
            cdf[0] = 0;
            size_t n = f.size();
            for (size_t i = 1; i < n + 1; ++i)
            {
                CHECK_GE(func[i - 1], 0);
                cdf[i] = cdf[i - 1] + func[i - 1] * (max - min) / n;
            }

            // Transform step function integral into CDF
            funcInt = cdf[n];
            if (funcInt == 0)
                for (size_t i = 1; i < n + 1; ++i)
                    cdf[i] = (Float)i / (Float)n;
            else
                for (size_t i = 1; i < n + 1; ++i)
                    cdf[i] /= funcInt;
        }

        public Float Integral() { return funcInt; }
        public size_t size() { return func.size(); }

        public Float Sample(Float u, Float* pdf = null, int* offset = null)
        {
            // Find surrounding CDF segments and _offset_
            int o = (int)FindInterval(cdf.size(), (int index) => cdf[index] <= u);
            if (offset != null)
                *offset = o;

            // Compute offset along CDF segment
            Float du = u - cdf[o];
            if (cdf[o + 1] - cdf[o] > 0)
                du /= cdf[o + 1] - cdf[o];
            DCHECK(!IsNaN(du));

            // Compute PDF for sampled offset
            if (pdf != null)
                *pdf = (funcInt > 0) ? func[o] / funcInt : 0;

            // Return $x$ corresponding to sample
            return Lerp((o + du) / size(), min, max);
        }

        public pstd.optional<Float> Invert(Float x)
        {
            // Compute offset to CDF values that bracket $x$
            if (x < min || x > max)
                return default;
            Float c = (x - min) / (max - min) * func.size();
            int offset = Clamp((int)c, 0, (int)func.size() - 1);
            DCHECK(offset >= 0 && (ulong)(offset + 1) < cdf.size());

            // Linearly interpolate between adjacent CDF values to find sample value
            Float delta = c - offset;
            return Lerp(delta, cdf[offset], cdf[offset + 1]);
        }

        public static void TestCompareDistributions(PiecewiseConstant1D da, PiecewiseConstant1D db, Float eps = 1e-5f)
        {
            CHECK_EQ(da.func.size(), db.func.size());
            CHECK_EQ(da.cdf.size(), db.cdf.size());
            CHECK_EQ(da.min, db.min);
            CHECK_EQ(da.max, db.max);
            for (ulong i = 0; i < da.func.size(); ++i)
            {
                Float pdfa = da.func[i] / da.funcInt, pdfb = db.func[i] / db.funcInt;
                Float err = std.abs(pdfa - pdfb) / ((pdfa + pdfb) / 2);
                CHECK(err < eps);
            }
        }

        // PiecewiseConstant1D Public Members
        public pstd.vector<Float> func, cdf;
        public Float min, max;
        public Float funcInt = 0;
    }

    // PiecewiseConstant2D Definition
    public unsafe class PiecewiseConstant2D
    {
        // PiecewiseConstant2D Public Methods
        public PiecewiseConstant2D()
        {
            pConditionalV = new pstd.vector<PiecewiseConstant1D>();
            pMarginal = new PiecewiseConstant1D();
        }
        public PiecewiseConstant2D(Allocator alloc) : this() { }
        public PiecewiseConstant2D(pstd.span<Float> data, int nx, int ny, Allocator alloc = null)
            : this(data, nx, ny, new Bounds2f(new Point2f(0, 0), new Point2f(1, 1)), alloc) { }
        public PiecewiseConstant2D(Array2D<Float> data, Allocator alloc = null)
            : this((pstd.span<Float>)data, data.XSize(), data.YSize(), alloc) { }
        public PiecewiseConstant2D(Array2D<Float> data, Bounds2f domain, Allocator alloc = null)
            : this((pstd.span<Float>)data, data.XSize(), data.YSize(), domain, alloc) { }

        public PiecewiseConstant2D(pstd.span<Float> func, int nu, int nv, Bounds2f domain, Allocator alloc = null)
        {
            this.domain = domain;
            pConditionalV = new pstd.vector<PiecewiseConstant1D>();
            CHECK_EQ(func.size(), (size_t)nu * (size_t)nv);
            pConditionalV.reserve((ulong)nv);
            for (int v = 0; v < nv; ++v)
                // Compute conditional sampling distribution for $\tilde{v}$
                pConditionalV.emplace_back(new PiecewiseConstant1D(func.subspan((ulong)(v * nu), (ulong)nu), domain.pMin[0],
                                           domain.pMax[0], alloc));

            // Compute marginal sampling distribution $p[\tilde{v}]$
            pstd.vector<Float> marginalFunc = new pstd.vector<Float>();
            marginalFunc.reserve((ulong)nv);
            for (int v = 0; v < nv; ++v)
                marginalFunc.push_back(pConditionalV[v].Integral());
            pMarginal =
                new PiecewiseConstant1D(marginalFunc, domain.pMin[1], domain.pMax[1], alloc);
        }

        public size_t BytesUsed()
        {
            return pConditionalV.size() *
                       (pConditionalV[0].BytesUsed() + (size_t)sizeof(PiecewiseConstant1D*)) +
                   pMarginal.BytesUsed();
        }

        public Bounds2f Domain() { return domain; }

        public Point2i Resolution()
        {
            return new Point2i((int)pConditionalV[0].size(), (int)pMarginal.size());
        }

        public override string ToString()
        {
            return $"[ PiecewiseConstant2D domain: {domain} pMarginal: {pMarginal} ]";
        }

        public Float Integral() { return pMarginal.Integral(); }

        public Point2f Sample(Point2f u, Float* pdf = null, Point2i* offset = null)
        {
            Float pdf0, pdf1;
            int uv0, uv1;
            Float d1 = pMarginal.Sample(u[1], &pdf1, &uv1);
            Float d0 = pConditionalV[uv1].Sample(u[0], &pdf0, &uv0);
            if (pdf != null)
                *pdf = pdf0 * pdf1;
            if (offset != null)
                *offset = new Point2i(uv0, uv1);
            return new Point2f(d0, d1);
        }

        public pstd.optional<Point2f> Invert(Point2f p)
        {
            pstd.optional<Float> mInv = pMarginal.Invert(p[1]);
            if (!mInv)
                return default;
            Float p1o = (p[1] - domain.pMin[1]) / (domain.pMax[1] - domain.pMin[1]);
            if (p1o < 0 || p1o > 1)
                return default;
            int offset = (int)Clamp(p1o * pConditionalV.size(), 0, (Float)(pConditionalV.size() - 1));
            pstd.optional<Float> cInv = pConditionalV[offset].Invert(p[0]);
            if (!cInv)
                return default;
            return new Point2f(cInv.value(), mInv.value());
        }

        public Float PDF(Point2f pr)
        {
            Vector2f po = domain.Offset(pr);
            Point2f p = new Point2f(po.x, po.y);
            int iu =
                Clamp((int)(p[0] * pConditionalV[0].size()), 0, (int)pConditionalV[0].size() - 1);
            int iv = Clamp((int)(p[1] * pMarginal.size()), 0, (int)pMarginal.size() - 1);
            return pConditionalV[iv].func[iu] / pMarginal.Integral();
        }

        public static void TestCompareDistributions(PiecewiseConstant2D da, PiecewiseConstant2D db, Float eps = 1e-5f)
        {
            PiecewiseConstant1D.TestCompareDistributions(da.pMarginal, db.pMarginal, eps);

            CHECK_EQ(da.pConditionalV.size(), db.pConditionalV.size());
            CHECK(da.domain == db.domain);
            for (ulong i = 0; i < da.pConditionalV.size(); ++i)
                PiecewiseConstant1D.TestCompareDistributions(da.pConditionalV[i],
                                                             db.pConditionalV[i], eps);
        }

        // PiecewiseConstant2D Private Members
        private Bounds2f domain;
        private pstd.vector<PiecewiseConstant1D> pConditionalV;
        private PiecewiseConstant1D pMarginal;
    }

    // AliasTable Definition
    public unsafe class AliasTable
    {
        // AliasTable Public Methods
        public AliasTable() { bins = new pstd.vector<Bin>(); }
        public AliasTable(Allocator alloc) : this() { }

        // AliasTable Method Definitions
        public AliasTable(pstd.span<Float> weights, Allocator alloc = null)
        {
            bins = new pstd.vector<Bin>(weights.size());
            // Normalize _weights_ to compute alias table PDF
            double dsum = 0.0;
            for (int i = 0; i < (int)weights.size(); ++i)
                dsum += weights[i];
            Float sum = (Float)dsum;
            CHECK_GT(sum, 0);
            for (ulong i = 0; i < weights.size(); ++i)
                bins[i].p = weights[i] / sum;

            // Create alias table work lists
            List<Outcome> under = new List<Outcome>(), over = new List<Outcome>();
            for (ulong i = 0; i < bins.size(); ++i)
            {
                // Add outcome _i_ to an alias table work list
                Float pHat = bins[i].p * bins.size();
                if (pHat < 1)
                    under.Add(new Outcome { pHat = pHat, index = i });
                else
                    over.Add(new Outcome { pHat = pHat, index = i });
            }

            // Process under and over work item together
            while (under.Count != 0 && over.Count != 0)
            {
                // Remove items _un_ and _ov_ from the alias table work lists
                Outcome un = under[under.Count - 1], ov = over[over.Count - 1];
                under.RemoveAt(under.Count - 1);
                over.RemoveAt(over.Count - 1);

                // Initialize probability and alias for _un_
                bins[un.index].q = un.pHat;
                bins[un.index].alias = (int)ov.index;

                // Push excess probability on to work list
                Float pExcess = un.pHat + ov.pHat - 1;
                if (pExcess < 1)
                    under.Add(new Outcome { pHat = pExcess, index = ov.index });
                else
                    over.Add(new Outcome { pHat = pExcess, index = ov.index });
            }

            // Handle remaining alias table work items
            while (over.Count != 0)
            {
                Outcome ov = over[over.Count - 1];
                over.RemoveAt(over.Count - 1);
                bins[ov.index].q = 1;
                bins[ov.index].alias = -1;
            }
            while (under.Count != 0)
            {
                Outcome un = under[under.Count - 1];
                under.RemoveAt(under.Count - 1);
                bins[un.index].q = 1;
                bins[un.index].alias = -1;
            }
        }

        public size_t size() { return bins.size(); }

        public int Sample(Float u, Float* pmf = null, Float* uRemapped = null)
        {
            // Compute alias table _offset_ and remapped random sample _up_
            int offset = std.min<int>((int)(u * bins.size()), (int)bins.size() - 1);
            Float up = std.min<Float>(u * bins.size() - offset, OneMinusEpsilon);

            if (up < bins[offset].q)
            {
                // Return sample for alias table at _offset_
                DCHECK_GT(bins[offset].p, 0);
                if (pmf != null)
                    *pmf = bins[offset].p;
                if (uRemapped != null)
                    *uRemapped = std.min<Float>(up / bins[offset].q, OneMinusEpsilon);
                return offset;
            }
            else
            {
                // Return sample for alias table at _alias[offset]_
                int alias = bins[offset].alias;
                DCHECK_GE(alias, 0);
                DCHECK_GT(bins[alias].p, 0);
                if (pmf != null)
                    *pmf = bins[alias].p;
                if (uRemapped != null)
                    *uRemapped = std.min<Float>((up - bins[offset].q) / (1 - bins[offset].q),
                                                OneMinusEpsilon);
                return alias;
            }
        }

        public override string ToString()
        {
            string s = "[ AliasTable bins: [ ";
            for (ulong i = 0; i < bins.size(); ++i)
                s += $"[ Bin q: {bins[i].q} p: {bins[i].p} alias: {bins[i].alias} ] ";
            return s + "] ]";
        }
        public Float PMF(int index) { return bins[index].p; }

        private struct Outcome
        {
            public Float pHat;
            public size_t index;
        }

        // AliasTable Private Members
        private struct Bin
        {
            public Float q, p;
            public int alias;
        }
        private pstd.vector<Bin> bins;
    }

    // SummedAreaTable Definition
    public unsafe class SummedAreaTable
    {
        // SummedAreaTable Public Methods
        public SummedAreaTable(Allocator alloc) { sum = new Array2D<double>(); }
        public SummedAreaTable(Array2D<Float> values, Allocator alloc = null)
        {
            sum = new Array2D<double>(values.XSize(), values.YSize(), alloc);
            sum[0, 0] = values[0, 0];
            // Compute sums along first row and column
            for (int x = 1; x < sum.XSize(); ++x)
                sum[x, 0] = values[x, 0] + sum[x - 1, 0];
            for (int y = 1; y < sum.YSize(); ++y)
                sum[0, y] = values[0, y] + sum[0, y - 1];

            // Compute sums for the remainder of the entries
            for (int y = 1; y < sum.YSize(); ++y)
                for (int x = 1; x < sum.XSize(); ++x)
                    sum[x, y] =
                        (values[x, y] + sum[x - 1, y] + sum[x, y - 1] - sum[x - 1, y - 1]);
        }

        public Float Integral(Bounds2f extent)
        {
            double s = (((double)Lookup(extent.pMax.x, extent.pMax.y) -
                         (double)Lookup(extent.pMin.x, extent.pMax.y)) +
                        ((double)Lookup(extent.pMin.x, extent.pMin.y) -
                         (double)Lookup(extent.pMax.x, extent.pMin.y)));
            return std.max<Float>((Float)(s / (sum.XSize() * sum.YSize())), 0);
        }

        // SummedAreaTable Private Methods
        private Float Lookup(Float x, Float y)
        {
            // Rescale $(x,y)$ to table resolution and compute integer coordinates
            x *= sum.XSize();
            y *= sum.YSize();
            int x0 = (int)x, y0 = (int)y;

            // Bilinearly interpolate between surrounding table values
            Float v00 = LookupInt(x0, y0), v10 = LookupInt(x0 + 1, y0);
            Float v01 = LookupInt(x0, y0 + 1), v11 = LookupInt(x0 + 1, y0 + 1);
            Float dx = x - (int)x, dy = y - (int)y;
            return (1 - dx) * (1 - dy) * v00 + (1 - dx) * dy * v01 + dx * (1 - dy) * v10 +
                   dx * dy * v11;
        }

        private Float LookupInt(int x, int y)
        {
            // Return zero at lower boundaries
            if (x == 0 || y == 0)
                return 0;

            // Reindex $(x,y)$ and return actual stored value
            x = std.min(x - 1, sum.XSize() - 1);
            y = std.min(y - 1, sum.YSize() - 1);
            return (Float)sum[x, y];
        }

        public override string ToString()
        {
            return $"[ SummedAreaTable sum: {sum} ]";
        }

        // SummedAreaTable Private Members
        private Array2D<double> sum;
    }

    // WindowedPiecewiseConstant2D Definition
    public unsafe class WindowedPiecewiseConstant2D
    {
        // WindowedPiecewiseConstant2D Public Methods
        public WindowedPiecewiseConstant2D(Allocator alloc)
        {
            sat = new SummedAreaTable(alloc);
            func = new Array2D<Float>();
        }
        public WindowedPiecewiseConstant2D(Array2D<Float> f, Allocator alloc = null)
        {
            sat = new SummedAreaTable(f, alloc);
            func = new Array2D<Float>(f, alloc);
        }

        public pstd.optional<Point2f> Sample(Point2f u, Bounds2f b, Float* pdf)
        {
            // Handle zero-valued function for windowed sampling
            if (sat.Integral(b) == 0)
                return default;

            // Define lambda function _Px_ for marginal cumulative distribution
            Float bInt = sat.Integral(b);
            Func<Float, Float> Px = (Float x) =>
            {
                Bounds2f bx = b;
                bx.pMax.x = x;
                return sat.Integral(bx) / bInt;
            };

            // Sample marginal windowed function in $x$
            Point2f p = new Point2f();
            p.x = SampleBisection(Px, u[0], b.pMin.x, b.pMax.x, func.XSize());

            // Sample conditional windowed function in $y$
            // Compute 2D bounds _bCond_ for conditional sampling
            int nx = func.XSize();
            Bounds2f bCond = new Bounds2f(new Point2f(pstd.floor(p.x * nx) / nx, b.pMin.y),
                                          new Point2f(pstd.ceil(p.x * nx) / nx, b.pMax.y));
            if (bCond.pMin.x == bCond.pMax.x)
                bCond.pMax.x += 1.0f / nx;
            if (sat.Integral(bCond) == 0)
                return default;

            // Define lambda function for conditional distribution and sample $y$
            Float condIntegral = sat.Integral(bCond);
            Bounds2f bCondCopy = bCond;
            Func<Float, Float> Py = (Float y) =>
            {
                Bounds2f by = bCondCopy;
                by.pMax.y = y;
                return sat.Integral(by) / condIntegral;
            };
            p.y = SampleBisection(Py, u[1], b.pMin.y, b.pMax.y, func.YSize());

            // Compute PDF and return point sampled from windowed function
            *pdf = Eval(p) / bInt;
            return p;
        }

        public Float PDF(Point2f p, Bounds2f b)
        {
            Float funcInt = sat.Integral(b);
            if (funcInt == 0)
                return 0;
            return Eval(p) / funcInt;
        }

        // WindowedPiecewiseConstant2D Private Methods
        private static Float SampleBisection(Func<Float, Float> P, Float u, Float min, Float max, int n)
        {
            // Apply bisection to bracket _u_
            while (pstd.ceil(n * max) - pstd.floor(n * min) > 1)
            {
                DCHECK_LE(P(min), u);
                DCHECK_GE(P(max), u);
                Float mid = (min + max) / 2;
                if (P(mid) > u)
                    max = mid;
                else
                    min = mid;
            }

            // Find sample by interpolating between _min_ and _max_
            Float t = (u - P(min)) / (P(max) - P(min));
            return Clamp(Lerp(t, min, max), min, max);
        }

        private Float Eval(Point2f p)
        {
            Point2i pi = new Point2i(std.min<int>((int)(p[0] * func.XSize()), func.XSize() - 1),
                                     std.min<int>((int)(p[1] * func.YSize()), func.YSize() - 1));
            return func[pi];
        }

        // WindowedPiecewiseConstant2D Private Members
        private SummedAreaTable sat;
        private Array2D<Float> func;
    }

    // detail::IndexingIterator and friends (sampling.h). C++'s begin()/end() range-for protocol is kept
    // (begin(), end(), Increment() for operator++, Deref() for operator*, ==/!=), and the generators also
    // implement IEnumerable<T> so that C# "foreach" works on them.
    // (C++ friend access to the generators' members is internal/public here; IndexingIterator's
    // templated generator pointer parameter is dropped since it is unused, and RNGIterator takes the
    // generator's sequenceIndex directly.)
    namespace detail
    {
        public abstract unsafe class IndexingIterator<Iterator> where Iterator : IndexingIterator<Iterator>
        {
            public IndexingIterator(int i, int n)
            {
                this.i = i;
                this.n = n;
            }

            public static bool operator ==(IndexingIterator<Iterator> a, IndexingIterator<Iterator> it)
            {
                return a.i == it.i;
            }
            public static bool operator !=(IndexingIterator<Iterator> a, IndexingIterator<Iterator> it)
            {
                return !(a == it);
            }
            // operator++()
            public Iterator Increment()
            {
                ++i;
                return (Iterator)this;
            }
            // operator++(int)
            public Iterator PostIncrement()
            {
                Iterator it = (Iterator)MemberwiseClone();
                return it.Increment();
            }

            protected int i, n;
        }

        // (The C++ Iterator(i, n, generator) construction is MakeIterator() here, since C# cannot call a
        // constructor with arguments on a type parameter.)
        public abstract unsafe class IndexingGenerator<Generator, Iterator> where Iterator : IndexingIterator<Iterator>
        {
            public IndexingGenerator(int n)
            {
                this.n = n;
            }
            public Iterator begin() { return MakeIterator(0); }
            public Iterator end() { return MakeIterator(n); }

            protected abstract Iterator MakeIterator(int i);

            protected int n;
        }

        public abstract unsafe class RNGGenerator<Generator, Iterator> : IndexingGenerator<Generator, Iterator>
            where Iterator : IndexingIterator<Iterator>
        {
            public RNGGenerator(int n, uint64_t sequenceIndex = 0, uint64_t seed = PCG32_DEFAULT_STATE)
                : base(n)
            {
                this.sequenceIndex = sequenceIndex;
                this.seed = seed;
            }

            // (protected with friend RNGIterator<Iterator> in C++)
            public uint64_t sequenceIndex, seed;
        }

        public abstract unsafe class RNGIterator<Iterator> : IndexingIterator<Iterator> where Iterator : IndexingIterator<Iterator>
        {
            public RNGIterator(int i, int n, uint64_t sequenceIndex)
                : base(i, n)
            {
                rng = new RNG(sequenceIndex);
            }

            protected RNG rng;
        }
    }

    public unsafe class Uniform1D : detail.RNGGenerator<Uniform1D, detail.Uniform1DIter>, IEnumerable<Float>
    {
        public Uniform1D(int n, uint64_t sequenceIndex = 0, uint64_t seed = PCG32_DEFAULT_STATE)
            : base(n, sequenceIndex, seed) { }
        protected override detail.Uniform1DIter MakeIterator(int i) { return new detail.Uniform1DIter(i, n, this); }
        public IEnumerator<Float> GetEnumerator()
        {
            for (detail.Uniform1DIter it = begin(), e = end(); it != e; it.Increment())
                yield return it.Deref();
        }
        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
    }

    public unsafe class Uniform2D : detail.RNGGenerator<Uniform2D, detail.Uniform2DIter>, IEnumerable<Point2f>
    {
        public Uniform2D(int n, uint64_t sequenceIndex = 0, uint64_t seed = PCG32_DEFAULT_STATE)
            : base(n, sequenceIndex, seed) { }
        protected override detail.Uniform2DIter MakeIterator(int i) { return new detail.Uniform2DIter(i, n, this); }
        public IEnumerator<Point2f> GetEnumerator()
        {
            for (detail.Uniform2DIter it = begin(), e = end(); it != e; it.Increment())
                yield return it.Deref();
        }
        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
    }

    public unsafe class Uniform3D : detail.RNGGenerator<Uniform3D, detail.Uniform3DIter>, IEnumerable<Point3f>
    {
        public Uniform3D(int n, uint64_t sequenceIndex = 0, uint64_t seed = PCG32_DEFAULT_STATE)
            : base(n, sequenceIndex, seed) { }
        protected override detail.Uniform3DIter MakeIterator(int i) { return new detail.Uniform3DIter(i, n, this); }
        public IEnumerator<Point3f> GetEnumerator()
        {
            for (detail.Uniform3DIter it = begin(), e = end(); it != e; it.Increment())
                yield return it.Deref();
        }
        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
    }

    public unsafe class Hammersley2D : detail.IndexingGenerator<Hammersley2D, detail.Hammersley2DIter>, IEnumerable<Point2f>
    {
        public Hammersley2D(int n) : base(n) { }
        protected override detail.Hammersley2DIter MakeIterator(int i) { return new detail.Hammersley2DIter(i, n, this); }
        public IEnumerator<Point2f> GetEnumerator()
        {
            for (detail.Hammersley2DIter it = begin(), e = end(); it != e; it.Increment())
                yield return it.Deref();
        }
        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
    }

    public unsafe class Hammersley3D : detail.IndexingGenerator<Hammersley3D, detail.Hammersley3DIter>, IEnumerable<Point3f>
    {
        public Hammersley3D(int n) : base(n) { }
        protected override detail.Hammersley3DIter MakeIterator(int i) { return new detail.Hammersley3DIter(i, n, this); }
        public IEnumerator<Point3f> GetEnumerator()
        {
            for (detail.Hammersley3DIter it = begin(), e = end(); it != e; it.Increment())
                yield return it.Deref();
        }
        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
    }

    public unsafe class Stratified1D : detail.RNGGenerator<Stratified1D, detail.Stratified1DIter>, IEnumerable<Float>
    {
        public Stratified1D(int n, uint64_t sequenceIndex = 0, uint64_t seed = PCG32_DEFAULT_STATE)
            : base(n, sequenceIndex, seed) { }
        protected override detail.Stratified1DIter MakeIterator(int i) { return new detail.Stratified1DIter(i, n, this); }
        public IEnumerator<Float> GetEnumerator()
        {
            for (detail.Stratified1DIter it = begin(), e = end(); it != e; it.Increment())
                yield return it.Deref();
        }
        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
    }

    public unsafe class Stratified2D : detail.RNGGenerator<Stratified2D, detail.Stratified2DIter>, IEnumerable<Point2f>
    {
        public Stratified2D(int nx, int ny, uint64_t sequenceIndex = 0, uint64_t seed = PCG32_DEFAULT_STATE)
            : base(nx * ny, sequenceIndex, seed)
        {
            this.nx = nx;
            this.ny = ny;
        }
        protected override detail.Stratified2DIter MakeIterator(int i) { return new detail.Stratified2DIter(i, n, this); }
        public IEnumerator<Point2f> GetEnumerator()
        {
            for (detail.Stratified2DIter it = begin(), e = end(); it != e; it.Increment())
                yield return it.Deref();
        }
        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }

        // (private with friend detail::Stratified2DIter in C++)
        internal int nx, ny;
    }

    public unsafe class Stratified3D : detail.RNGGenerator<Stratified3D, detail.Stratified3DIter>, IEnumerable<Point3f>
    {
        public Stratified3D(int nx, int ny, int nz, uint64_t sequenceIndex = 0, uint64_t seed = PCG32_DEFAULT_STATE)
            : base(nx * ny * nz, sequenceIndex, seed)
        {
            this.nx = nx;
            this.ny = ny;
            this.nz = nz;
        }
        protected override detail.Stratified3DIter MakeIterator(int i) { return new detail.Stratified3DIter(i, n, this); }
        public IEnumerator<Point3f> GetEnumerator()
        {
            for (detail.Stratified3DIter it = begin(), e = end(); it != e; it.Increment())
                yield return it.Deref();
        }
        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }

        // (private with friend detail::Stratified3DIter in C++)
        internal int nx, ny, nz;
    }

    namespace detail
    {
        public unsafe class Uniform1DIter : RNGIterator<Uniform1DIter>
        {
            public Uniform1DIter(int i, int n, Uniform1D generator) : base(i, n, generator.sequenceIndex) { }
            // operator*()
            public Float Deref() { return rng.Uniform<Float>(); }
        }

        public unsafe class Uniform2DIter : RNGIterator<Uniform2DIter>
        {
            public Uniform2DIter(int i, int n, Uniform2D generator) : base(i, n, generator.sequenceIndex) { }
            public Point2f Deref()
            {
                // braced-init-list: evaluated left to right
                Float x = rng.Uniform<Float>();
                Float y = rng.Uniform<Float>();
                return new Point2f(x, y);
            }
        }

        public unsafe class Uniform3DIter : RNGIterator<Uniform3DIter>
        {
            public Uniform3DIter(int i, int n, Uniform3D generator) : base(i, n, generator.sequenceIndex) { }
            public Point3f Deref()
            {
                Float x = rng.Uniform<Float>();
                Float y = rng.Uniform<Float>();
                Float z = rng.Uniform<Float>();
                return new Point3f(x, y, z);
            }
        }

        public unsafe class Stratified1DIter : RNGIterator<Stratified1DIter>
        {
            public Stratified1DIter(int i, int n, Stratified1D generator) : base(i, n, generator.sequenceIndex) { }
            public Float Deref() { return (i + rng.Uniform<Float>()) / n; }
        }

        public unsafe class Stratified2DIter : RNGIterator<Stratified2DIter>
        {
            public Stratified2DIter(int i, int n, Stratified2D generator)
                : base(i, n, generator.sequenceIndex)
            {
                nx = generator.nx;
                ny = generator.ny;
            }

            public Point2f Deref()
            {
                int ix = i % nx, iy = i / nx;
                Float a = rng.Uniform<Float>();
                Float b = rng.Uniform<Float>();
                return new Point2f((ix + a) / nx, (iy + b) / ny);
            }

            private int nx, ny;
        }

        public unsafe class Stratified3DIter : RNGIterator<Stratified3DIter>
        {
            public Stratified3DIter(int i, int n, Stratified3D generator)
                : base(i, n, generator.sequenceIndex)
            {
                nx = generator.nx;
                ny = generator.ny;
                nz = generator.nz;
            }

            public Point3f Deref()
            {
                int ix = i % nx;
                int iy = (i / nx) % ny;
                int iz = i / (nx * ny);
                Float a = rng.Uniform<Float>();
                Float b = rng.Uniform<Float>();
                Float c = rng.Uniform<Float>();
                return new Point3f((ix + a) / nx, (iy + b) / ny, (iz + c) / nz);
            }

            private int nx, ny, nz;
        }

        public unsafe class Hammersley2DIter : IndexingIterator<Hammersley2DIter>
        {
            public Hammersley2DIter(int i, int n, Hammersley2D generator) : base(i, n) { }
            public Point2f Deref() { return new Point2f((Float)i / (Float)n, pbrt.RadicalInverse(0, (uint64_t)i)); }
        }

        public unsafe class Hammersley3DIter : IndexingIterator<Hammersley3DIter>
        {
            public Hammersley3DIter(int i, int n, Hammersley3D generator) : base(i, n) { }
            public Point3f Deref()
            {
                return new Point3f((Float)i / (Float)n, pbrt.RadicalInverse(0, (uint64_t)i),
                                   pbrt.RadicalInverse(1, (uint64_t)i));
            }
        }
    }

    // PiecewiseLinear2D Implementation
    // *****************************************************************************
    // Marginal-conditional warp
    // *****************************************************************************
    public struct PLSample
    {
        public Point2f p;
        public Float pdf;
    }

    // C++ template parameter Dimension is a constructor argument here.
    public unsafe class PiecewiseLinear2D
    {
        private int Dimension;
        private int ArraySize;

        public PiecewiseLinear2D(int Dimension, Allocator alloc)
        {
            this.Dimension = Dimension;
            ArraySize = Dimension;
            m_param_size = new uint32_t[Math.Max(ArraySize, 1)];
            m_param_strides = new uint32_t[Math.Max(ArraySize, 1)];
            m_param_values = new float[Math.Max(ArraySize, 1)][];
            for (int i = 0; i < ArraySize; ++i)
                m_param_values[i] = new float[0];
            m_data = new float[0];
            m_marginal_cdf = new float[0];
            m_conditional_cdf = new float[0];
        }

        public PiecewiseLinear2D(int Dimension, Allocator alloc, float[] data, int xSize, int ySize,
                                 int[] param_res = null, float[][] param_values = null,
                                 bool normalize = true, bool build_cdf = true)
            : this(Dimension, alloc)
        {
            m_size = new Vector2i(xSize, ySize);
            m_patch_size = new Vector2f(1.0f / (xSize - 1), 1.0f / (ySize - 1));
            m_inv_patch_size = new Vector2f(m_size.x - 1, m_size.y - 1);

            if (build_cdf && !normalize)
                LOG_FATAL("PiecewiseLinear2D: build_cdf implies normalize=true");

            /* Keep track of the dependence on additional parameters (optional) */
            uint32_t slices = 1;
            for (int i = Dimension - 1; i >= 0; --i)
            {
                if (param_res[i] < 1)
                    LOG_FATAL("PiecewiseLinear2D(): parameter resolution must be >= 1!");

                m_param_size[i] = (uint32_t)param_res[i];
                m_param_values[i] = new float[param_res[i]];
                Array.Copy(param_values[i], m_param_values[i], param_res[i]);
                m_param_strides[i] = param_res[i] > 1 ? slices : 0;
                slices *= m_param_size[i];
            }

            uint32_t n_values = (uint32_t)(xSize * ySize);

            m_data = new float[slices * n_values];

            int dataIn = 0;
            if (build_cdf)
            {
                m_marginal_cdf = new float[slices * m_size.y];
                m_conditional_cdf = new float[slices * n_values];

                float[] marginal_cdf = m_marginal_cdf, conditional_cdf = m_conditional_cdf, data_out = m_data;
                int marginal_cdf_o = 0, conditional_cdf_o = 0, data_out_o = 0;

                for (uint32_t slice = 0; slice < slices; ++slice)
                {
                    /* Construct conditional CDF */
                    for (int y = 0; y < m_size.y; ++y)
                    {
                        double sum = 0.0;
                        size_t i = (size_t)(y * xSize);
                        conditional_cdf[conditional_cdf_o + (int)i] = 0.0f;
                        for (int x = 0; x < m_size.x - 1; ++x, ++i)
                        {
                            sum += .5 * ((double)data[dataIn + (int)i] + (double)data[dataIn + (int)i + 1]);
                            conditional_cdf[conditional_cdf_o + (int)i + 1] = (float)sum;
                        }
                    }

                    /* Construct marginal CDF */
                    marginal_cdf[marginal_cdf_o + 0] = 0.0f;
                    double sum2 = 0.0;
                    for (int y = 0; y < m_size.y - 1; ++y)
                    {
                        sum2 += .5 * ((double)conditional_cdf[conditional_cdf_o + (y + 1) * xSize - 1] +
                                      (double)conditional_cdf[conditional_cdf_o + (y + 2) * xSize - 1]);
                        marginal_cdf[marginal_cdf_o + y + 1] = (float)sum2;
                    }

                    /* Normalize CDFs and PDF (if requested) */
                    float normalization = 1.0f / marginal_cdf[marginal_cdf_o + m_size.y - 1];
                    for (uint32_t i = 0; i < n_values; ++i)
                        conditional_cdf[conditional_cdf_o + i] *= normalization;
                    for (int i = 0; i < m_size.y; ++i)
                        marginal_cdf[marginal_cdf_o + i] *= normalization;
                    for (uint32_t i = 0; i < n_values; ++i)
                        data_out[data_out_o + i] = data[dataIn + i] * normalization;

                    marginal_cdf_o += m_size.y;
                    conditional_cdf_o += (int)n_values;
                    data_out_o += (int)n_values;
                    dataIn += (int)n_values;
                }
            }
            else
            {
                float[] data_out = m_data;
                int data_out_o = 0;

                for (uint32_t slice = 0; slice < slices; ++slice)
                {
                    float normalization = 1.0f / HProd(m_inv_patch_size);
                    if (normalize)
                    {
                        double sum = 0.0;
                        for (int y = 0; y < m_size.y - 1; ++y)
                        {
                            int i = y * xSize;
                            for (int x = 0; x < m_size.x - 1; ++x, ++i)
                            {
                                float v00 = data[dataIn + i], v10 = data[dataIn + i + 1], v01 = data[dataIn + i + xSize],
                                      v11 = data[dataIn + i + 1 + xSize],
                                      avg = .25f * (v00 + v10 + v01 + v11);
                                sum += (double)avg;
                            }
                        }
                        normalization = (float)(1.0 / sum);
                    }

                    for (uint32_t k = 0; k < n_values; ++k)
                        data_out[data_out_o + k] = data[dataIn + k] * normalization;

                    dataIn += (int)n_values;
                    data_out_o += (int)n_values;
                }
            }
        }

        private void ParamLookup(Float[] param, float[] param_weight, out uint32_t slice_offset)
        {
            slice_offset = 0u;
            for (int dim = 0; dim < Dimension; ++dim)
            {
                if (m_param_size[dim] == 1)
                {
                    param_weight[2 * dim] = 1.0f;
                    param_weight[2 * dim + 1] = 0.0f;
                    continue;
                }

                int d = dim;
                Float pd = param[d];
                uint32_t param_index = (uint32_t)FindInterval(m_param_size[d], (int idx) =>
                {
                    return m_param_values[d][idx] <= pd;
                });

                Float p0 = m_param_values[dim][param_index],
                      p1 = m_param_values[dim][param_index + 1];

                param_weight[2 * dim + 1] = Clamp((param[dim] - p0) / (p1 - p0), 0, 1);
                param_weight[2 * dim] = 1.0f - param_weight[2 * dim + 1];
                slice_offset += m_param_strides[dim] * param_index;
            }
        }

        /**
         * Given a uniformly distributed 2D sample, draw a sample from the
         * distribution (parameterized by param if applicable)
         *
         * Returns the warped sample and associated probability density.
         */
        public PLSample Sample(Point2f sample, params Float[] param)
        {
            DCHECK_EQ(param.Length, Dimension);

            /* Avoid degeneracies at the extrema */
            sample[0] = Clamp(sample[0], 1 - OneMinusEpsilon, OneMinusEpsilon);
            sample[1] = Clamp(sample[1], 1 - OneMinusEpsilon, OneMinusEpsilon);

            /* Look up parameter-related indices and weights (if Dimension != 0) */
            float[] param_weight = new float[2 * Math.Max(ArraySize, 1)];
            uint32_t slice_offset;
            ParamLookup(param, param_weight, out slice_offset);

            /* Sample the row first */
            uint32_t offset = 0;
            if (Dimension != 0)
                offset = slice_offset * (uint32_t)m_size.y;

            uint32_t offset0 = offset;
            Func<int, float> fetch_marginal = (int idx) =>
            {
                return lookup(Dimension, m_marginal_cdf, 0, offset0 + (uint32_t)idx, (uint32_t)m_size.y,
                              param_weight);
            };

            uint32_t row = (uint32_t)FindInterval((size_t)m_size.y, (int idx) => fetch_marginal(idx) < sample.y);

            sample.y -= fetch_marginal((int)row);

            uint32_t slice_size = (uint32_t)HProd(m_size);
            offset = row * (uint32_t)m_size.x;
            if (Dimension != 0)
                offset += slice_offset * slice_size;

            Float r0 = lookup(Dimension, m_conditional_cdf, 0, offset + (uint32_t)m_size.x - 1,
                              slice_size, param_weight),
                  r1 = lookup(Dimension, m_conditional_cdf, 0, offset + ((uint32_t)m_size.x * 2 - 1),
                              slice_size, param_weight);

            bool is_const = std.abs(r0 - r1) < 1e-4f * (r0 + r1);
            sample.y = is_const ? (2.0f * sample.y)
                                : (r0 - SafeSqrt(r0 * r0 - 2.0f * sample.y * (r0 - r1)));
            sample.y /= is_const ? (r0 + r1) : (r0 - r1);

            /* Sample the column next */
            sample.x *= (1.0f - sample.y) * r0 + sample.y * r1;

            uint32_t offset1 = offset;
            Float sy = sample.y;
            Func<int, float> fetch_conditional = (int idx) =>
            {
                float v0 = lookup(Dimension, m_conditional_cdf, 0, offset1 + (uint32_t)idx,
                                  slice_size, param_weight),
                      v1 = lookup(Dimension, m_conditional_cdf, m_size.x, offset1 + (uint32_t)idx,
                                  slice_size, param_weight);

                return (1.0f - sy) * v0 + sy * v1;
            };

            uint32_t col = (uint32_t)FindInterval((size_t)m_size.x, (int idx) => fetch_conditional(idx) < sample.x);

            sample.x -= fetch_conditional((int)col);

            offset += col;

            Float v00 = lookup(Dimension, m_data, 0, offset, slice_size, param_weight),
                  v10 = lookup(Dimension, m_data, 1, offset, slice_size, param_weight),
                  v01 = lookup(Dimension, m_data, m_size.x, offset, slice_size, param_weight),
                  v11 = lookup(Dimension, m_data, m_size.x + 1, offset, slice_size, param_weight),
                  c0 = FMA((1.0f - sample.y), v00, sample.y * v01),
                  c1 = FMA((1.0f - sample.y), v10, sample.y * v11);

            is_const = std.abs(c0 - c1) < 1e-4f * (c0 + c1);
            sample.x = is_const ? (2.0f * sample.x)
                                : (c0 - SafeSqrt(c0 * c0 - 2.0f * sample.x * (c0 - c1)));
            sample.x /= is_const ? (c0 + c1) : (c0 - c1);

            PLSample result;
            result.p = new Point2f((col + sample.x) * m_patch_size.x, (row + sample.y) * m_patch_size.y);
            result.pdf = ((1.0f - sample.x) * c0 + sample.x * c1) * HProd(m_inv_patch_size);
            return result;
        }

        /// Inverse of the mapping implemented in Sample()
        public PLSample Invert(Point2f sample, params Float[] param)
        {
            DCHECK_EQ(param.Length, Dimension);

            /* Look up parameter-related indices and weights (if Dimension != 0) */
            float[] param_weight = new float[2 * Math.Max(ArraySize, 1)];
            uint32_t slice_offset;
            ParamLookup(param, param_weight, out slice_offset);

            /* Fetch values at corners of bilinear patch */
            sample.x *= m_inv_patch_size.x;
            sample.y *= m_inv_patch_size.y;
            Vector2i pos = Min(new Vector2i((int)sample.x, (int)sample.y), m_size - new Vector2i(2, 2));
            sample -= new Vector2f(pos.x, pos.y);

            uint32_t offset = (uint32_t)(pos.x + pos.y * m_size.x);
            uint32_t slice_size = (uint32_t)HProd(m_size);
            if (Dimension != 0)
                offset += slice_offset * slice_size;

            /* Invert the X component */
            Float v00 = lookup(Dimension, m_data, 0, offset, slice_size, param_weight),
                  v10 = lookup(Dimension, m_data, 1, offset, slice_size, param_weight),
                  v01 = lookup(Dimension, m_data, m_size.x, offset, slice_size, param_weight),
                  v11 = lookup(Dimension, m_data, m_size.x + 1, offset, slice_size, param_weight);

            Vector2f w1 = new Vector2f(sample.x, sample.y), w0 = new Vector2f(1, 1) - w1;

            Float c0 = FMA(w0.y, v00, w1.y * v01), c1 = FMA(w0.y, v10, w1.y * v11),
                  pdf = FMA(w0.x, c0, w1.x * c1);

            sample.x *= c0 + .5f * sample.x * (c1 - c0);

            Float v0 = lookup(Dimension, m_conditional_cdf, 0, offset, slice_size, param_weight),
                  v1 = lookup(Dimension, m_conditional_cdf, m_size.x, offset, slice_size, param_weight);

            sample.x += (1.0f - sample.y) * v0 + sample.y * v1;

            offset = (uint32_t)(pos.y * m_size.x);
            if (Dimension != 0)
                offset += slice_offset * slice_size;

            Float r0 = lookup(Dimension, m_conditional_cdf, 0, offset + (uint32_t)m_size.x - 1,
                              slice_size, param_weight),
                  r1 = lookup(Dimension, m_conditional_cdf, 0, offset + ((uint32_t)m_size.x * 2 - 1),
                              slice_size, param_weight);

            sample.x /= (1.0f - sample.y) * r0 + sample.y * r1;

            /* Invert the Y component */
            sample.y *= r0 + .5f * sample.y * (r1 - r0);

            offset = (uint32_t)pos.y;
            if (Dimension != 0)
                offset += slice_offset * (uint32_t)m_size.y;

            sample.y +=
                lookup(Dimension, m_marginal_cdf, 0, offset, (uint32_t)m_size.y, param_weight);

            PLSample result;
            result.p = sample;
            result.pdf = pdf * HProd(m_inv_patch_size);
            return result;
        }

        /**
         * Evaluate the density at position pos. The distribution is
         * parameterized by param if applicable.
         */
        public float Evaluate(Point2f pos, params Float[] param)
        {
            DCHECK_EQ(param.Length, Dimension);

            /* Look up parameter-related indices and weights (if Dimension != 0) */
            float[] param_weight = new float[2 * Math.Max(ArraySize, 1)];
            uint32_t slice_offset;
            ParamLookup(param, param_weight, out slice_offset);

            /* Compute linear interpolation weights */
            pos.x *= m_inv_patch_size.x;
            pos.y *= m_inv_patch_size.y;
            Vector2i offset = Min(new Vector2i((int)pos.x, (int)pos.y), m_size - new Vector2i(2, 2));

            Vector2f w1 = new Vector2f(pos.x, pos.y) - new Vector2f(offset.x, offset.y),
                     w0 = new Vector2f(1, 1) - w1;

            uint32_t index = (uint32_t)(offset.x + offset.y * m_size.x);

            uint32_t size = (uint32_t)HProd(m_size);
            if (Dimension != 0)
                index += slice_offset * size;

            Float v00 = lookup(Dimension, m_data, 0, index, size, param_weight),
                  v10 = lookup(Dimension, m_data, 1, index, size, param_weight),
                  v01 = lookup(Dimension, m_data, m_size.x, index, size, param_weight),
                  v11 = lookup(Dimension, m_data, m_size.x + 1, index, size, param_weight);

            return FMA(w0.y, FMA(w0.x, v00, w1.x * v10), w1.y * FMA(w0.x, v01, w1.x * v11)) *
                   HProd(m_inv_patch_size);
        }

        public size_t BytesUsed()
        {
            size_t sum = 4 * ((size_t)m_data.Length + (size_t)m_marginal_cdf.Length +
                              (size_t)m_conditional_cdf.Length);
            for (int i = 0; i < ArraySize; ++i)
                sum += (size_t)m_param_values[i].Length;
            return sum;
        }

        // lookup<Dim>() unrolled recursion: Dim from Dimension down to 0.
        // dataOffset corresponds to the pointer arithmetic on the data pointer in C++.
        private Float lookup(int Dim, float[] data, int dataOffset, uint32_t i0, uint32_t size, float[] param_weight)
        {
            if (Dim == 0)
                return data[dataOffset + (int)i0];

            uint32_t i1 = i0 + m_param_strides[Dim - 1] * size;

            Float w0 = param_weight[2 * Dim - 2], w1 = param_weight[2 * Dim - 1],
                  v0 = lookup(Dim - 1, data, dataOffset, i0, size, param_weight),
                  v1 = lookup(Dim - 1, data, dataOffset, i1, size, param_weight);

            return FMA(v0, w0, v1 * w1);
        }

        /// Resolution of the discretized density function
        private Vector2i m_size;

        /// Size of a bilinear patch in the unit square
        private Vector2f m_patch_size, m_inv_patch_size;

        /// Resolution of each parameter (optional)
        private uint32_t[] m_param_size;

        /// Stride per parameter in units of sizeof(float)
        private uint32_t[] m_param_strides;

        /// Discretization of each parameter domain
        private float[][] m_param_values;

        /// Density values
        private float[] m_data;

        /// Marginal and conditional PDFs
        private float[] m_marginal_cdf;
        private float[] m_conditional_cdf;
    }
}
