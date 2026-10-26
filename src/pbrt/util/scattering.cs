// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0

using System;
using System.Runtime.CompilerServices;

namespace pbrt
{
    public static unsafe partial class Globals
    {
        // Scattering Inline Functions
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3f Reflect(Vector3f wo, Vector3f n)
        {
            return -wo + 2 * Dot(wo, n) * n;
        }

        public static bool Refract(Vector3f wi, Normal3f n, Float eta, Float* etap, Vector3f* wt)
        {
            Float cosTheta_i = Dot(n, wi);
            // Potentially flip interface orientation for Snell's law
            if (cosTheta_i < 0)
            {
                eta = 1 / eta;
                cosTheta_i = -cosTheta_i;
                n = -n;
            }

            // Compute $\cos\,\theta_\roman{t}$ using Snell's law
            Float sin2Theta_i = std.max<Float>(0, 1 - Sqr(cosTheta_i));
            Float sin2Theta_t = sin2Theta_i / Sqr(eta);
            // Handle total internal reflection case
            if (sin2Theta_t >= 1)
                return false;

            Float cosTheta_t = std.sqrt(1 - sin2Theta_t);

            *wt = -wi / eta + (cosTheta_i / eta - cosTheta_t) * (Vector3f)n;
            // Provide relative IOR along ray to caller
            if (etap != null)
                *etap = eta;

            return true;
        }

        public static Float HenyeyGreenstein(Float cosTheta, Float g)
        {
            // The Henyey-Greenstein phase function isn't suitable for |g| \approx
            // 1 so we clamp it before it becomes numerically instable. (It's an
            // analogous situation to BSDFs: if the BSDF is perfectly specular, one
            // should use one based on a Dirac delta distribution rather than a
            // very smooth microfacet distribution...)
            g = Clamp(g, (Float)(-.99), (Float).99);
            Float denom = 1 + Sqr(g) + 2 * g * cosTheta;
            return Inv4Pi * (1 - Sqr(g)) / (denom * SafeSqrt(denom));
        }

        // Fresnel Inline Functions
        public static Float FrDielectric(Float cosTheta_i, Float eta)
        {
            cosTheta_i = Clamp(cosTheta_i, -1, 1);
            // Potentially flip interface orientation for Fresnel equations
            if (cosTheta_i < 0)
            {
                eta = 1 / eta;
                cosTheta_i = -cosTheta_i;
            }

            // Compute $\cos\,\theta_\roman{t}$ for Fresnel equations using Snell's law
            Float sin2Theta_i = 1 - Sqr(cosTheta_i);
            Float sin2Theta_t = sin2Theta_i / Sqr(eta);
            if (sin2Theta_t >= 1)
                return 1.0f;
            Float cosTheta_t = SafeSqrt(1 - sin2Theta_t);

            Float r_parl = (eta * cosTheta_i - cosTheta_t) / (eta * cosTheta_i + cosTheta_t);
            Float r_perp = (cosTheta_i - eta * cosTheta_t) / (cosTheta_i + eta * cosTheta_t);
            return (Sqr(r_parl) + Sqr(r_perp)) / 2;
        }

        public static Float FrComplex(Float cosTheta_i, pstd.complex eta)
        {
            cosTheta_i = Clamp(cosTheta_i, 0, 1);
            // Compute complex $\cos\,\theta_\roman{t}$ for Fresnel equations using Snell's law
            Float sin2Theta_i = 1 - Sqr(cosTheta_i);
            pstd.complex sin2Theta_t = sin2Theta_i / Sqr(eta);
            pstd.complex cosTheta_t = pstd.sqrt(1 - sin2Theta_t);

            pstd.complex r_parl = (eta * cosTheta_i - cosTheta_t) / (eta * cosTheta_i + cosTheta_t);
            pstd.complex r_perp = (cosTheta_i - eta * cosTheta_t) / (cosTheta_i + eta * cosTheta_t);
            return (pstd.norm(r_parl) + pstd.norm(r_perp)) / 2;
        }

        public static SampledSpectrum FrComplex(Float cosTheta_i, SampledSpectrum eta, SampledSpectrum k)
        {
            SampledSpectrum result = new SampledSpectrum();
            for (int i = 0; i < NSpectrumSamples; ++i)
                result[i] = FrComplex(cosTheta_i, new pstd.complex(eta[i], k[i]));
            return result;
        }

        // BSSRDF Utility Functions
        public static Float FresnelMoment1(Float eta)
        {
            Float eta2 = eta * eta, eta3 = eta2 * eta, eta4 = eta3 * eta, eta5 = eta4 * eta;
            if (eta < 1)
                return 0.45966f - 1.73965f * eta + 3.37668f * eta2 - (Float)3.904945 * eta3 +
                       2.49277f * eta4 - 0.68441f * eta5;
            else
                return -4.61686f + 11.1136f * eta - 10.4646f * eta2 + 5.11455f * eta3 -
                       1.27198f * eta4 + 0.12746f * eta5;
        }

        public static Float FresnelMoment2(Float eta)
        {
            Float eta2 = eta * eta, eta3 = eta2 * eta, eta4 = eta3 * eta, eta5 = eta4 * eta;
            if (eta < 1)
            {
                return 0.27614f - 0.87350f * eta + 1.12077f * eta2 - 0.65095f * eta3 +
                       0.07883f * eta4 + 0.04860f * eta5;
            }
            else
            {
                Float r_eta = 1 / eta, r_eta2 = r_eta * r_eta, r_eta3 = r_eta2 * r_eta;
                return -547.033f + 45.3087f * r_eta3 - 218.725f * r_eta2 + 458.843f * r_eta +
                       404.557f * eta - 189.519f * eta2 + 54.9327f * eta3 - 9.00603f * eta4 +
                       0.63942f * eta5;
            }
        }
    }

    // TrowbridgeReitzDistribution Definition
    public unsafe struct TrowbridgeReitzDistribution
    {
        // TrowbridgeReitzDistribution Public Methods
        public TrowbridgeReitzDistribution(Float ax, Float ay)
        {
            alpha_x = ax;
            alpha_y = ay;
            if (!EffectivelySmooth())
            {
                // If one direction has some roughness, then the other can't
                // have zero (or very low) roughness; the computation of |e| in
                // D() blows up in that case.
                alpha_x = std.max<Float>(alpha_x, 1e-4f);
                alpha_y = std.max<Float>(alpha_y, 1e-4f);
            }
        }

        public Float D(Vector3f wm)
        {
            Float tan2Theta = Tan2Theta(wm);
            if (IsInf(tan2Theta))
                return 0;
            Float cos4Theta = Sqr(Cos2Theta(wm));
            if (cos4Theta < 1e-16f)
                return 0;
            Float e = tan2Theta * (Sqr(CosPhi(wm) / alpha_x) + Sqr(SinPhi(wm) / alpha_y));
            return 1 / (Pi * alpha_x * alpha_y * cos4Theta * Sqr(1 + e));
        }

        public bool EffectivelySmooth() { return std.max(alpha_x, alpha_y) < 1e-3f; }

        public Float G1(Vector3f w) { return 1 / (1 + Lambda(w)); }

        public Float Lambda(Vector3f w)
        {
            Float tan2Theta = Tan2Theta(w);
            if (IsInf(tan2Theta))
                return 0;
            Float alpha2 = Sqr(CosPhi(w) * alpha_x) + Sqr(SinPhi(w) * alpha_y);
            return (std.sqrt(1 + alpha2 * tan2Theta) - 1) / 2;
        }

        public Float G(Vector3f wo, Vector3f wi) { return 1 / (1 + Lambda(wo) + Lambda(wi)); }

        public Float D(Vector3f w, Vector3f wm)
        {
            return G1(w) / AbsCosTheta(w) * D(wm) * AbsDot(w, wm);
        }

        public Float PDF(Vector3f w, Vector3f wm) { return D(w, wm); }

        public Vector3f Sample_wm(Vector3f w, Point2f u)
        {
            // Transform _w_ to hemispherical configuration
            Vector3f wh = Normalize(new Vector3f(alpha_x * w.x, alpha_y * w.y, w.z));
            if (wh.z < 0)
                wh = -wh;

            // Find orthonormal basis for visible normal sampling
            Vector3f T1 = (wh.z < 0.99999f) ? Normalize(Cross(new Vector3f(0, 0, 1), wh))
                                            : new Vector3f(1, 0, 0);
            Vector3f T2 = Cross(wh, T1);

            // Generate uniformly distributed points on the unit disk
            Point2f p = SampleUniformDiskPolar(u);

            // Warp hemispherical projection for visible normal sampling
            Float h = std.sqrt(1 - Sqr(p.x));
            p.y = Lerp((1 + wh.z) / 2, h, p.y);

            // Reproject to hemisphere and transform normal to ellipsoid configuration
            Float pz = std.sqrt(std.max<Float>(0, 1 - LengthSquared(new Vector2f(p.x, p.y))));
            Vector3f nh = p.x * T1 + p.y * T2 + pz * wh;
            CHECK_RARE(1e-5f, nh.z == 0);
            return Normalize(
                new Vector3f(alpha_x * nh.x, alpha_y * nh.y, std.max<Float>(1e-6f, nh.z)));
        }

        public override string ToString()
        {
            return $"[ TrowbridgeReitzDistribution alpha_x: {alpha_x} alpha_y: {alpha_y} ]";
        }

        // Note that this should probably instead be "return Sqr(roughness)" to
        // be more perceptually uniform, though this wasn't noticed until some
        // time after pbrt-v4 shipped: https://github.com/mmp/pbrt-v4/issues/479.
        // therefore, we will leave it as is so that the rendered results with
        // existing pbrt-v4 scenes doesn't change unexpectedly.
        public static Float RoughnessToAlpha(Float roughness) { return std.sqrt(roughness); }

        public void Regularize()
        {
            if (alpha_x < 0.3f)
                alpha_x = Clamp(2 * alpha_x, 0.1f, 0.3f);
            if (alpha_y < 0.3f)
                alpha_y = Clamp(2 * alpha_y, 0.1f, 0.3f);
        }

        // TrowbridgeReitzDistribution Private Members
        private Float alpha_x, alpha_y;
    }
}
