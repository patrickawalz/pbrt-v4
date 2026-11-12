// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0

using System;
using System.Runtime.CompilerServices;

namespace pbrt
{
    public static unsafe partial class pbrt
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

    // MicrofacetDistributionType Definition
    // (not in pbrt-v4) The distributions that a material can ask for by name ("string distribution")
    public enum MicrofacetDistributionType
    {
        TrowbridgeReitz,
        Beckmann,
        StudentT
    }

    // MicrofacetDistribution Definition
    // (not in pbrt-v4, which has TrowbridgeReitzDistribution alone) The interface of the microfacet distributions, as the
    // MicrofacetDistribution of pbrt-v3 was, with the names of pbrt-v4. The normals of the microfacets are in the upper
    // hemisphere (wm.z > 0).
    //
    // A direction below the horizon (w.z < 0) is met in two ways, and the interface has both.
    //   Two sided       G1, G, D(w, wm), PDF, Sample_wm: the convention of pbrt-v4. The direction is seen as one above the
    //                   other face of the surface, and everything is mirrored. What the BxDFs of pbrt-v4 call.
    //   Single sided    Sigma, PDF_SingleSided, Sample_wm_SingleSided: the microfacets face upwards only, and a direction below
    //                   the horizon sees those that are turned towards it. This is the case of a shading normal that faces away
    //                   from a direction above the true surface, and the normalisation is that of Tokuyoshi, "Unbiased VNDF
    //                   Sampling for Backfacing Shading Normals" (SIGGRAPH 2021 Talks): the density of the visible normals is
    //                   D(wm) max(0, w . wm) / Sigma(w), with Sigma(w) the integral of D(wm) max(0, w . wm) over the normals.
    // The two agree for a direction above the horizon, where Sigma(w) = cos(theta) (1 + Lambda(w)).
    public abstract unsafe class MicrofacetDistribution
    {
        // "ggx" (or "trowbridgereitz"), "beckmann", "studentt"
        public static MicrofacetDistributionType ParseType(string name, FileLoc loc)
        {
            if (name == "ggx" || name == "trowbridgereitz")
            {
                return MicrofacetDistributionType.TrowbridgeReitz;
            }
            if (name == "beckmann")
            {
                return MicrofacetDistributionType.Beckmann;
            }
            if (name == "studentt")
            {
                return MicrofacetDistributionType.StudentT;
            }
            ErrorExit(loc, StringPrintf("%s: microfacet distribution unknown (\"ggx\", \"beckmann\" and \"studentt\" are known).", name));
            return MicrofacetDistributionType.TrowbridgeReitz;
        }

        // gamma is the shape of the Student's t distribution, and is not used by the others
        public static MicrofacetDistribution Create(MicrofacetDistributionType type, Float ax, Float ay, Float gamma)
        {
            switch (type)
            {
            case MicrofacetDistributionType.Beckmann:
                return new BeckmannDistribution(ax, ay);
            case MicrofacetDistributionType.StudentT:
                return new StudentTDistribution(ax, ay, gamma);
            default:
                return new TrowbridgeReitzDistribution(ax, ay);
            }
        }

        // MicrofacetDistribution Interface
        public abstract Float D(Vector3f wm);
        public abstract Float Lambda(Vector3f w);
        public abstract Float Sigma(Vector3f w);
        public abstract Vector3f Sample_wm_SingleSided(Vector3f w, Point2f u);
        public abstract bool EffectivelySmooth();
        public abstract void Regularize();

        // MicrofacetDistribution Public Methods
        public Float G1(Vector3f w)
        {
            return 1 / (1 + Lambda(w));
        }

        public Float G(Vector3f wo, Vector3f wi)
        {
            return 1 / (1 + Lambda(wo) + Lambda(wi));
        }

        public Float D(Vector3f w, Vector3f wm)
        {
            return G1(w) / AbsCosTheta(w) * D(wm) * AbsDot(w, wm);
        }

        public Float PDF(Vector3f w, Vector3f wm)
        {
            return D(w, wm);
        }

        public Vector3f Sample_wm(Vector3f w, Point2f u)
        {
            return Sample_wm_SingleSided(w.z < 0 ? -w : w, u);
        }

        public Float PDF_SingleSided(Vector3f w, Vector3f wm)
        {
            Float sigma = Sigma(w);
            if (!(sigma > 0) || !(wm.z > 0))
            {
                return 0;
            }
            return std.max<Float>(Dot(w, wm), 0) * D(wm) / sigma;
        }

        // MicrofacetDistribution V-Cavity Interface
        // The slopes of the unit distribution (alpha = 1), which is what a distribution is once the directions are stretched by
        // its roughness. P2 is the marginal density of the slope along one axis.
        //   UnitSlopeCdf(x)       the integral of P2 below x
        //   UnitSlopeMoment(x)    minus the integral of s P2(s) below x (positive)
        //   SampleUnitSlopeAcross the slope along the other axis, given the slope x along the first
        public abstract double UnitSlopeCdf(double x);
        public abstract double UnitSlopeDensity(double x);
        public abstract double UnitSlopeMoment(double x);
        public abstract double SampleUnitSlopeAcross(double x, double u);

        // MicrofacetDistribution V-Cavity Methods
        // The V-cavity masking of Torrance and Sparrow, for the same distributions, with the single sided normalisation of
        // Tokuyoshi 2021 (supplementary document, section 2) for a direction below the horizon. The masking of a microfacet is
        //     G(w, wm) = min(2 (wm . n) |w . n| / (w . wm), 1) for w . wm > 0,
        // the probability that a ray along w meets the microsurface is H(w) (1 above the horizon), and the density of the
        // visible normals is G(w, wm) D(wm) (w . wm) / (H(w) |w . n|). Below the horizon it has two forms, on either side of the
        // slope 3 cot(theta) along the direction: H and the sampling are written with the slopes of the unit distribution, so
        // that they hold for every distribution (the document gives them for GGX and Beckmann).
        public Float G_VCavity(Vector3f w, Vector3f wm)
        {
            Float cosine = Dot(w, wm);
            if (!(cosine > 0) || !(wm.z > 0))
            {
                return 0;
            }
            return std.min<Float>(1, 2 * wm.z * std.abs(w.z) / cosine);
        }

        public Float HitProbability_VCavity(Vector3f w)
        {
            if (w.z >= 0)
            {
                return 1;
            }
            double length, sinTheta, cosTheta, cosPhi, sinPhi;
            Stretch(w, out length, out sinTheta, out cosTheta, out cosPhi, out sinPhi);
            if (sinTheta < 1e-9)
            {
                return 0;
            }
            double cot = cosTheta / sinTheta;
            double boundary = 3 * cot;
            double h = 2 * UnitSlopeCdf(boundary) +
                       length * (UnitVisibleSlopeCdf(sinTheta, cosTheta, cot) - UnitVisibleSlopeCdf(sinTheta, cosTheta, boundary)) / -w.z;
            return (Float)Math.Max(h, 0.0);
        }

        public Float PDF_VCavity(Vector3f w, Vector3f wm)
        {
            Float h = HitProbability_VCavity(w);
            if (!(h > 0) || w.z == 0)
            {
                return 0;
            }
            return G_VCavity(w, wm) * D(wm) * Dot(w, wm) / (h * std.abs(w.z));
        }

        public Vector3f Sample_wm_VCavity(Vector3f w, Float uc, Point2f u)
        {
            if (w.z >= 0)
            {
                // Heitz and d'Eon 2014: a normal of the distribution, or its mirror image across the groove, by the area
                // each shows to the direction
                Vector3f normal1 = Sample_wm_SingleSided(new Vector3f(0, 0, 1), u);
                Vector3f normal2 = new Vector3f(-normal1.x, -normal1.y, normal1.z);
                Float a1 = std.max<Float>(Dot(w, normal1), 0), a2 = std.max<Float>(Dot(w, normal2), 0);
                if (!(a1 + a2 > 0))
                {
                    return normal1;
                }
                return uc * (a1 + a2) < a1 ? normal1 : normal2;
            }
            double length, sinTheta, cosTheta, cosPhi, sinPhi;
            Stretch(w, out length, out sinTheta, out cosTheta, out cosPhi, out sinPhi);
            if (sinTheta < 1e-9)
            {
                return new Vector3f(0, 0, 1);
            }
            double cot = cosTheta / sinTheta;
            double boundary = 3 * cot;
            double below = UnitSlopeCdf(boundary);
            double visibleLow = UnitVisibleSlopeCdf(sinTheta, cosTheta, boundary), visibleHigh = UnitVisibleSlopeCdf(sinTheta, cosTheta, cot);
            double masked = 2 * below, unmasked = length * (visibleHigh - visibleLow) / -w.z;
            if (!(masked + unmasked > 0))
            {
                return new Vector3f(0, 0, 1);
            }
            double slopeX;
            if (uc * (masked + unmasked) < masked)
            {
                // The density proportional to D(wm) (wm . n), over the slopes below the boundary
                slopeX = InvertUnitSlopeCdf(u[0] * below, boundary);
            }
            else
            {
                // The density proportional to D(wm) (w . wm), over the slopes from the boundary to cot(theta)
                slopeX = InvertUnitVisibleSlopeCdf(sinTheta, cosTheta, visibleLow + u[0] * (visibleHigh - visibleLow), boundary, cot);
            }
            double slopeY = SampleUnitSlopeAcross(slopeX, Clamp(u[1], 1e-7f, OneMinusEpsilon));
            double x = cosPhi * slopeX - sinPhi * slopeY;
            double y = sinPhi * slopeX + cosPhi * slopeY;
            return Normalize(new Vector3f((Float)(-AlphaX() * x), (Float)(-AlphaY() * y), 1));
        }

        public abstract Float AlphaX();
        public abstract Float AlphaY();

        // The integral of (cos(theta) - s sin(theta)) P2(s) over the slopes s below x
        public double UnitVisibleSlopeCdf(double sinTheta, double cosTheta, double x)
        {
            return cosTheta * UnitSlopeCdf(x) + sinTheta * UnitSlopeMoment(x);
        }

        // The direction stretched by the roughness: its length, its polar angle and its azimuth
        private void Stretch(Vector3f w, out double length, out double sinTheta, out double cosTheta, out double cosPhi, out double sinPhi)
        {
            double dx = (double)AlphaX() * w.x, dy = (double)AlphaY() * w.y, dz = w.z;
            length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            double planar = Math.Sqrt(dx * dx + dy * dy);
            sinTheta = planar / length;
            cosTheta = dz / length;
            cosPhi = planar > 0 ? dx / planar : 1;
            sinPhi = planar > 0 ? dy / planar : 0;
        }

        // The slope below `high` at which UnitSlopeCdf is `target`. The slope is tan(psi), and psi is found in its finite
        // interval by a Newton iteration kept inside a bracket.
        private double InvertUnitSlopeCdf(double target, double high)
        {
            double low = -0.5 * Math.PI, top = Math.Atan(high);
            double psi = 0.5 * (low + top);
            for (int i = 0; i < 80; ++i)
            {
                double x = Math.Tan(psi);
                double value = UnitSlopeCdf(x) - target;
                if (value > 0)
                {
                    top = psi;
                }
                else
                {
                    low = psi;
                }
                if (top - low < 1e-11)
                {
                    break;
                }
                double cosPsi = Math.Cos(psi);
                double derivative = UnitSlopeDensity(x) / (cosPsi * cosPsi);
                double next = derivative > 0 ? psi - value / derivative : low - 1;
                psi = (next > low && next < top) ? next : 0.5 * (low + top);
            }
            return Math.Tan(psi);
        }

        // The slope between `from` and `to` at which UnitVisibleSlopeCdf is `target`
        private double InvertUnitVisibleSlopeCdf(double sinTheta, double cosTheta, double target, double from, double to)
        {
            double low = Math.Atan(from), top = Math.Atan(to);
            double psi = 0.5 * (low + top);
            for (int i = 0; i < 80; ++i)
            {
                double x = Math.Tan(psi);
                double value = UnitVisibleSlopeCdf(sinTheta, cosTheta, x) - target;
                if (value > 0)
                {
                    top = psi;
                }
                else
                {
                    low = psi;
                }
                if (top - low < 1e-11)
                {
                    break;
                }
                double cosPsi = Math.Cos(psi);
                double derivative = (cosTheta - x * sinTheta) * UnitSlopeDensity(x) / (cosPsi * cosPsi);
                double next = derivative > 0 ? psi - value / derivative : low - 1;
                psi = (next > low && next < top) ? next : 0.5 * (low + top);
            }
            return Math.Tan(psi);
        }
    }

    // TrowbridgeReitzDistribution Definition
    public sealed unsafe class TrowbridgeReitzDistribution : MicrofacetDistribution
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

        public override Float D(Vector3f wm)
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

        public override bool EffectivelySmooth() { return std.max(alpha_x, alpha_y) < 1e-3f; }

        public override Float Lambda(Vector3f w)
        {
            Float tan2Theta = Tan2Theta(w);
            if (IsInf(tan2Theta))
                return 0;
            Float alpha2 = Sqr(CosPhi(w) * alpha_x) + Sqr(SinPhi(w) * alpha_y);
            return (std.sqrt(1 + alpha2 * tan2Theta) - 1) / 2;
        }

        // (w.z + sqrt(alpha_x^2 w.x^2 + alpha_y^2 w.y^2 + w.z^2)) / 2 (Tokuyoshi 2021, supplementary document, section 1). Below
        // the horizon the sum cancels, and its product with the conjugate is used.
        public override Float Sigma(Vector3f w)
        {
            Float len2 = Sqr(alpha_x * w.x) + Sqr(alpha_y * w.y);
            Float t = std.sqrt(len2 + Sqr(w.z));
            if (w.z >= 0)
            {
                return (t + w.z) / 2;
            }
            return len2 / (2 * (t - w.z));
        }

        // The visible normals by spherical caps (Dupuy and Benyoub, "Sampling Visible GGX Normals with Spherical Caps", 2023),
        // with the height of the cap that of the single sided cross section, so that it holds for a direction below the horizon
        public override Vector3f Sample_wm_SingleSided(Vector3f w, Point2f u)
        {
            Float len2 = Sqr(alpha_x * w.x) + Sqr(alpha_y * w.y);
            Float t = std.sqrt(len2 + Sqr(w.z));
            Float invT = 1 / t;
            Float sx = alpha_x * w.x * invT, sy = alpha_y * w.y * invT;
            // 1 + z of the stretched direction; below the horizon the cancellation-free 2 Sigma / t
            Float capHeight = (w.z >= 0 ? t + w.z : len2 / (t - w.z)) * invT;
            Float phi = 2 * Pi * u[0];
            Float cap = u[1] * capHeight;
            Float sinTheta = std.sqrt(std.max<Float>(0, cap * (2 - cap)));
            Float hz = (1 - u[1]) * capHeight;
            Float hx = sx + sinTheta * std.cos(phi);
            Float hy = sy + sinTheta * std.sin(phi);
            Float px = alpha_x * hx, py = alpha_y * hy;
            Float scale = std.max(std.abs(px), std.max(std.abs(py), hz));
            if (!(scale > 0))
            {
                return new Vector3f(0, 0, 1);
            }
            Float inv = 1 / scale;
            return Normalize(new Vector3f(px * inv, py * inv, hz * inv));
        }

        // Note that this should probably instead be "return Sqr(roughness)" to
        // be more perceptually uniform, though this wasn't noticed until some
        // time after pbrt-v4 shipped: https://github.com/mmp/pbrt-v4/issues/479.
        // therefore, we will leave it as is so that the rendered results with
        // existing pbrt-v4 scenes doesn't change unexpectedly.
        public static Float RoughnessToAlpha(Float roughness) { return std.sqrt(roughness); }

        public override void Regularize()
        {
            if (alpha_x < 0.3f)
                alpha_x = Clamp(2 * alpha_x, 0.1f, 0.3f);
            if (alpha_y < 0.3f)
                alpha_y = Clamp(2 * alpha_y, 0.1f, 0.3f);
        }

        public override string ToString()
        {
            return $"[ TrowbridgeReitzDistribution alpha_x: {alpha_x} alpha_y: {alpha_y} ]";
        }

        // The slopes of the unit distribution: P2(x) = (1 + x^2)^(-3/2) / 2
        public override Float AlphaX() { return alpha_x; }
        public override Float AlphaY() { return alpha_y; }

        public override double UnitSlopeCdf(double x)
        {
            // (1 + x / sqrt(1 + x^2)) / 2, without the cancellation of a large negative slope
            double root = Math.Sqrt(1 + x * x);
            return x < 0 ? 0.5 / (root * (root - x)) : 0.5 * (1 + x / root);
        }

        public override double UnitSlopeDensity(double x)
        {
            return 0.5 / ((1 + x * x) * Math.Sqrt(1 + x * x));
        }

        public override double UnitSlopeMoment(double x)
        {
            return 0.5 / Math.Sqrt(1 + x * x);
        }

        // The slope across is sqrt(1 + x^2) tan(psi), with psi of density (2 / pi) cos^2(psi)
        public override double SampleUnitSlopeAcross(double x, double u)
        {
            double low = -0.5 * Math.PI, high = 0.5 * Math.PI;
            double psi = Math.PI * (u - 0.5);
            for (int i = 0; i < 60; ++i)
            {
                double value = 0.5 + (psi + Math.Sin(psi) * Math.Cos(psi)) / Math.PI - u;
                if (value > 0)
                {
                    high = psi;
                }
                else
                {
                    low = psi;
                }
                if (high - low < 1e-11)
                {
                    break;
                }
                double cosPsi = Math.Cos(psi);
                double derivative = 2 * cosPsi * cosPsi / Math.PI;
                double next = derivative > 0 ? psi - value / derivative : low - 1;
                psi = (next > low && next < high) ? next : 0.5 * (low + high);
            }
            return Math.Sqrt(1 + x * x) * Math.Tan(psi);
        }

        // TrowbridgeReitzDistribution Private Members
        private Float alpha_x, alpha_y;
    }

    // BeckmannDistribution Definition
    // (not in pbrt-v4) The Beckmann distribution of pbrt-v3, with two changes that the single sided normalisation asks for. Its
    // Lambda is the exact one (pbrt-v3 has a rational approximation, with which the visible normals do not integrate to 1), and
    // its visible slopes are sampled by solving their cumulative distribution (Heitz and d'Eon 2014, as Jakob 2014 and Tokuyoshi
    // 2021 do, by a guarded Newton iteration), which holds for a direction below the horizon. In double precision: below the
    // horizon the cross section is the difference of two nearly equal terms.
    public sealed unsafe class BeckmannDistribution : MicrofacetDistribution
    {
        // BeckmannDistribution Public Methods
        public BeckmannDistribution(Float ax, Float ay)
        {
            alpha_x = ax;
            alpha_y = ay;
            if (!EffectivelySmooth())
            {
                alpha_x = std.max<Float>(alpha_x, 1e-4f);
                alpha_y = std.max<Float>(alpha_y, 1e-4f);
            }
        }

        public override Float D(Vector3f wm)
        {
            Float tan2Theta = Tan2Theta(wm);
            if (IsInf(tan2Theta))
                return 0;
            Float cos4Theta = Sqr(Cos2Theta(wm));
            if (cos4Theta < 1e-16f)
                return 0;
            Float e = tan2Theta * (Sqr(CosPhi(wm) / alpha_x) + Sqr(SinPhi(wm) / alpha_y));
            return std.exp(-e) / (Pi * alpha_x * alpha_y * cos4Theta);
        }

        public override bool EffectivelySmooth() { return std.max(alpha_x, alpha_y) < 1e-3f; }

        // (erf(a) - 1) / 2 + exp(-a^2) / (2 a sqrt(pi)), a = 1 / (alpha |tan(theta)|)
        public override Float Lambda(Vector3f w)
        {
            Float tan2Theta = Tan2Theta(w);
            if (IsInf(tan2Theta))
                return 0;
            double alpha = Math.Sqrt(Sqr(CosPhi(w) * alpha_x) + Sqr(SinPhi(w) * alpha_y));
            double a = 1 / (alpha * Math.Sqrt(tan2Theta));
            if (!(a < 1e8))
                return 0;
            return (Float)(-0.5 * Erfc(a) + Math.Exp(-a * a) / (2 * a * Math.Sqrt(Math.PI)));
        }

        // (w.z erfc(-w.z / sqrt(B)) + sqrt(B / pi) exp(-w.z^2 / B)) / 2, B = alpha_x^2 w.x^2 + alpha_y^2 w.y^2 (Tokuyoshi 2021,
        // supplementary document, section 1)
        public override Float Sigma(Vector3f w)
        {
            double b = Sqr((double)alpha_x * w.x) + Sqr((double)alpha_y * w.y);
            if (b == 0)
            {
                return std.max<Float>(w.z, 0);
            }
            double rootB = Math.Sqrt(b);
            double z = w.z / rootB;
            return (Float)((w.z * Erfc(-z) + rootB / Math.Sqrt(Math.PI) * Math.Exp(-z * z)) / 2);
        }

        public override Vector3f Sample_wm_SingleSided(Vector3f w, Point2f u)
        {
            // Stretch the direction: the slopes are then those of the distribution with alpha = 1
            double dx = (double)alpha_x * w.x, dy = (double)alpha_y * w.y, dz = w.z;
            double length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            dx /= length;
            dy /= length;
            dz /= length;
            double sinTheta = Math.Sqrt(dx * dx + dy * dy);

            // The slope along the direction, by its cumulative distribution; the slope across is Gaussian
            double slopeX = SampleVisibleSlope(sinTheta, dz, u[0]);
            double slopeY = ErfInv(2 * Clamp(u[1], 1e-6f, OneMinusEpsilon) - 1);

            // Rotate to the azimuth of the direction, unstretch, and take the normal of the slope
            double cosPhi = sinTheta > 0 ? dx / sinTheta : 1, sinPhi = sinTheta > 0 ? dy / sinTheta : 0;
            double x = cosPhi * slopeX - sinPhi * slopeY;
            double y = sinPhi * slopeX + cosPhi * slopeY;
            return Normalize(new Vector3f((Float)(-alpha_x * x), (Float)(-alpha_y * y), 1));
        }

        public override void Regularize()
        {
            if (alpha_x < 0.3f)
                alpha_x = Clamp(2 * alpha_x, 0.1f, 0.3f);
            if (alpha_y < 0.3f)
                alpha_y = Clamp(2 * alpha_y, 0.1f, 0.3f);
        }

        public override string ToString()
        {
            return $"[ BeckmannDistribution alpha_x: {alpha_x} alpha_y: {alpha_y} ]";
        }

        // BeckmannDistribution Private Methods
        // The integral of (cos(theta) - s sin(theta)) exp(-s^2) / sqrt(pi) over the slopes s below x: the part of the cross
        // section of the unit distribution that the slopes below x make
        private static double VisibleSlopeCdf(double sinTheta, double cosTheta, double x)
        {
            return (cosTheta * Erfc(-x) + sinTheta / Math.Sqrt(Math.PI) * Math.Exp(-x * x)) / 2;
        }

        // A slope with the density (cos(theta) - x sin(theta)) exp(-x^2), over the slopes that face the direction (x below
        // cot(theta))
        private static double SampleVisibleSlope(double sinTheta, double cosTheta, double u)
        {
            // The slopes that matter: 6 standard deviations around 0 and, below the horizon, under cot(theta)
            double upper = sinTheta > 1e-12 * Math.Abs(cosTheta) ? cosTheta / sinTheta : (cosTheta > 0 ? 6 : -6);
            double high = Math.Min(upper, 6);
            double low = Math.Min(upper, 0) - 6;
            double cdfLow = VisibleSlopeCdf(sinTheta, cosTheta, low), cdfHigh = VisibleSlopeCdf(sinTheta, cosTheta, high);
            double target = cdfLow + u * (cdfHigh - cdfLow);

            double x = 0.5 * (low + high);
            for (int i = 0; i < 60; ++i)
            {
                double value = VisibleSlopeCdf(sinTheta, cosTheta, x) - target;
                if (value > 0)
                {
                    high = x;
                }
                else
                {
                    low = x;
                }
                if (high - low < 1e-9)
                {
                    break;
                }
                // Newton, kept inside the bracket
                double derivative = (cosTheta - x * sinTheta) * Math.Exp(-x * x) / Math.Sqrt(Math.PI);
                double next = derivative > 0 ? x - value / derivative : low - 1;
                x = (next > low && next < high) ? next : 0.5 * (low + high);
            }
            return x;
        }

        // The slopes of the unit distribution: P2(x) = exp(-x^2) / sqrt(pi)
        public override Float AlphaX() { return alpha_x; }
        public override Float AlphaY() { return alpha_y; }

        public override double UnitSlopeCdf(double x)
        {
            return 0.5 * Erfc(-x);
        }

        public override double UnitSlopeDensity(double x)
        {
            return Math.Exp(-x * x) / Math.Sqrt(Math.PI);
        }

        public override double UnitSlopeMoment(double x)
        {
            return Math.Exp(-x * x) / (2 * Math.Sqrt(Math.PI));
        }

        // The two slopes are independent
        public override double SampleUnitSlopeAcross(double x, double u)
        {
            return ErfInv((Float)(2 * u - 1));
        }

        // BeckmannDistribution Private Members
        private Float alpha_x, alpha_y;
    }
    // StudentTDistribution Definition
    // (not in pbrt-v4) The Student's t distribution of slopes (STD) of Ribardiere, Bringier, Meneveaux and Simonot, "STD:
    // Student's t-Distribution of Slopes for Microfacet Based BSDFs" (Computer Graphics Forum 36(2), 2017), in the
    // parametrisation of the authors' Mitsuba plugin:
    //     D(wm) = 1 / (pi alpha_x alpha_y cos^4(theta) (1 + e / (gamma - 1))^gamma),   e = tan^2(theta) (cos^2(phi) / alpha_x^2 + sin^2(phi) / alpha_y^2).
    // gamma = 2 is the Trowbridge-Reitz (GGX) distribution, and gamma to infinity the Beckmann one; gamma is above 3/2 (the
    // cross section is infinite at 3/2).
    //
    // The plugin has the Smith Lambda by a hypergeometric function, and samples the visible normals of GGX or of Beckmann in
    // its place. Here both come from one function, the cross section of the unit distribution (alpha = 1), so that they are
    // exact, consistent with each other, and hold below the horizon. With the direction in the xz plane at the angle theta,
    // the slope x along it has the marginal P2(x) = c (1 + x^2 / (gamma - 1))^(1/2 - gamma), a Student's t distribution, and
    //     S(x) = cos(theta) F(x) + sin(theta) c (gamma - 1) / (2 gamma - 3) (1 + x^2 / (gamma - 1))^(3/2 - gamma)
    // is the integral of (cos(theta) - s sin(theta)) P2(s) over the slopes s below x (F is the cumulative distribution of P2,
    // an incomplete beta function). S(cot(theta)) is the cross section, and S is the cumulative distribution of the visible
    // slope, which is inverted. The slope across is a Student's t distribution again, inverted in the same way.
    public sealed unsafe class StudentTDistribution : MicrofacetDistribution
    {
        // StudentTDistribution Public Methods
        public StudentTDistribution(Float ax, Float ay, Float gamma)
        {
            alpha_x = ax;
            alpha_y = ay;
            if (!EffectivelySmooth())
            {
                alpha_x = std.max<Float>(alpha_x, 1e-4f);
                alpha_y = std.max<Float>(alpha_y, 1e-4f);
            }
            this.gamma = Math.Max((double)gamma, 1.51);
            // c: the marginal integrates to 1. The integral of (1 + x^2 / (gamma - 1))^(1/2 - gamma) is
            // sqrt(gamma - 1) sqrt(pi) Gamma(gamma - 1) / Gamma(gamma - 1/2)
            marginal = Math.Exp(LogGamma(this.gamma - 0.5) - LogGamma(this.gamma - 1)) / Math.Sqrt(Math.PI * (this.gamma - 1));
            // The slope across: the density proportional to (1 + t^2)^(-gamma) integrates to sqrt(pi) Gamma(gamma - 1/2) / Gamma(gamma)
            across = Math.Exp(LogGamma(this.gamma) - LogGamma(this.gamma - 0.5)) / Math.Sqrt(Math.PI);
        }

        // Without the cutoff near the horizon that the Trowbridge-Reitz D of pbrt has: below gamma = 2 the distribution grows
        // without bound there (as cos(theta)^(2 gamma - 4)), and what a cutoff removes is a part of the cross section
        public override Float D(Vector3f wm)
        {
            // As for the other distributions, the normal may be given on either side (the BxDFs of pbrt pass wo + wi as it is)
            if (wm.z == 0 || IsNaN(wm.z))
                return 0;
            double cos2Theta = (double)wm.z * wm.z;
            double e = (Sqr((double)wm.x / alpha_x) + Sqr((double)wm.y / alpha_y)) / cos2Theta;
            return (Float)(Math.Pow(1 + e / (gamma - 1), -gamma) / (Math.PI * alpha_x * alpha_y * cos2Theta * cos2Theta));
        }

        public override bool EffectivelySmooth() { return std.max(alpha_x, alpha_y) < 1e-3f; }

        public override Float Lambda(Vector3f w)
        {
            Float cosTheta = AbsCosTheta(w);
            if (cosTheta == 0)
                return 0;
            return std.max<Float>(0, Sigma(new Vector3f(w.x, w.y, cosTheta)) / cosTheta - 1);
        }

        public override Float Sigma(Vector3f w)
        {
            double dx = (double)alpha_x * w.x, dy = (double)alpha_y * w.y, dz = w.z;
            double length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            double sinTheta = Math.Sqrt(dx * dx + dy * dy) / length, cosTheta = dz / length;
            if (sinTheta < 1e-9)
            {
                return std.max<Float>(w.z, 0);
            }
            return (Float)(length * VisibleSlopeCdf(sinTheta, cosTheta, cosTheta / sinTheta));
        }

        public override Vector3f Sample_wm_SingleSided(Vector3f w, Point2f u)
        {
            // Stretch the direction: the slopes are then those of the distribution with alpha = 1
            double dx = (double)alpha_x * w.x, dy = (double)alpha_y * w.y, dz = w.z;
            double length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            dx /= length;
            dy /= length;
            dz /= length;
            double sinTheta = Math.Sqrt(dx * dx + dy * dy);

            // The slope along the direction, by its cumulative distribution, and the slope across given it
            double slopeX = SampleVisibleSlope(sinTheta, dz, u[0]);
            double slopeY = Math.Sqrt((gamma - 1) + slopeX * slopeX) * SampleAcross(u[1]);

            // Rotate to the azimuth of the direction, unstretch, and take the normal of the slope
            double cosPhi = sinTheta > 0 ? dx / sinTheta : 1, sinPhi = sinTheta > 0 ? dy / sinTheta : 0;
            double x = cosPhi * slopeX - sinPhi * slopeY;
            double y = sinPhi * slopeX + cosPhi * slopeY;
            return Normalize(new Vector3f((Float)(-alpha_x * x), (Float)(-alpha_y * y), 1));
        }

        public override void Regularize()
        {
            if (alpha_x < 0.3f)
                alpha_x = Clamp(2 * alpha_x, 0.1f, 0.3f);
            if (alpha_y < 0.3f)
                alpha_y = Clamp(2 * alpha_y, 0.1f, 0.3f);
        }

        public override string ToString()
        {
            return $"[ StudentTDistribution alpha_x: {alpha_x} alpha_y: {alpha_y} gamma: {gamma} ]";
        }

        // StudentTDistribution Private Methods
        // F(x): the part of the marginal P2 below x. With z = 1 / (1 + x^2 / (gamma - 1)), the tail beyond |x| is
        // I_z(gamma - 1, 1/2) / 2.
        private double MarginalCdf(double x)
        {
            double tail = 0.5 * IncompleteBeta(gamma - 1, 0.5, 1 / (1 + x * x / (gamma - 1)));
            return x < 0 ? tail : 1 - tail;
        }

        // S(x) of the comment of the class
        private double VisibleSlopeCdf(double sinTheta, double cosTheta, double x)
        {
            double a = 1 + x * x / (gamma - 1);
            return cosTheta * MarginalCdf(x) + sinTheta * marginal * (gamma - 1) / (2 * gamma - 3) * Math.Pow(a, 1.5 - gamma);
        }

        // A slope with the density (cos(theta) - x sin(theta)) P2(x), over the slopes that face the direction (x below
        // cot(theta)). The slope is sqrt(gamma - 1) tan(psi), and psi is found in its finite interval.
        private double SampleVisibleSlope(double sinTheta, double cosTheta, double u)
        {
            double scale = Math.Sqrt(gamma - 1);
            double low = -0.5 * Math.PI;
            double high = sinTheta > 1e-12 * Math.Abs(cosTheta) ? Math.Atan2(cosTheta, sinTheta * scale) : (cosTheta > 0 ? 0.5 * Math.PI : low);
            double total = sinTheta > 1e-12 * Math.Abs(cosTheta) ? VisibleSlopeCdf(sinTheta, cosTheta, cosTheta / sinTheta) : Math.Max(cosTheta, 0);
            double target = u * total;

            double psi = 0.5 * (low + high);
            for (int i = 0; i < 60; ++i)
            {
                double x = scale * Math.Tan(psi);
                double value = VisibleSlopeCdf(sinTheta, cosTheta, x) - target;
                if (value > 0)
                {
                    high = psi;
                }
                else
                {
                    low = psi;
                }
                if (high - low < 1e-10)
                {
                    break;
                }
                // Newton, kept inside the bracket
                double cosPsi = Math.Cos(psi);
                double derivative = (cosTheta - x * sinTheta) * marginal * Math.Pow(1 + x * x / (gamma - 1), 0.5 - gamma) * scale / (cosPsi * cosPsi);
                double next = derivative > 0 ? psi - value / derivative : low - 1;
                psi = (next > low && next < high) ? next : 0.5 * (low + high);
            }
            return scale * Math.Tan(psi);
        }

        // A value t with the density proportional to (1 + t^2)^(-gamma). With t = tan(psi) the tail beyond |t| is
        // I_(cos^2(psi))(gamma - 1/2, 1/2) / 2.
        private double SampleAcross(double u)
        {
            double low = -0.5 * Math.PI, high = 0.5 * Math.PI;
            double psi = Math.PI * (u - 0.5);
            for (int i = 0; i < 60; ++i)
            {
                double cosPsi = Math.Cos(psi);
                double tail = 0.5 * IncompleteBeta(gamma - 0.5, 0.5, cosPsi * cosPsi);
                double value = (psi < 0 ? tail : 1 - tail) - u;
                if (value > 0)
                {
                    high = psi;
                }
                else
                {
                    low = psi;
                }
                if (high - low < 1e-10)
                {
                    break;
                }
                double derivative = across * Math.Pow(cosPsi, 2 * gamma - 2);
                double next = derivative > 0 ? psi - value / derivative : low - 1;
                psi = (next > low && next < high) ? next : 0.5 * (low + high);
            }
            return Math.Tan(psi);
        }

        // The slopes of the unit distribution: P2(x) = c (1 + x^2 / (gamma - 1))^(1/2 - gamma)
        public override Float AlphaX() { return alpha_x; }
        public override Float AlphaY() { return alpha_y; }

        public override double UnitSlopeCdf(double x)
        {
            return MarginalCdf(x);
        }

        public override double UnitSlopeDensity(double x)
        {
            return marginal * Math.Pow(1 + x * x / (gamma - 1), 0.5 - gamma);
        }

        public override double UnitSlopeMoment(double x)
        {
            return marginal * (gamma - 1) / (2 * gamma - 3) * Math.Pow(1 + x * x / (gamma - 1), 1.5 - gamma);
        }

        public override double SampleUnitSlopeAcross(double x, double u)
        {
            return Math.Sqrt((gamma - 1) + x * x) * SampleAcross(u);
        }

        // StudentTDistribution Private Members
        private Float alpha_x, alpha_y;
        private double gamma;
        // The constants of the marginal of the slope along the direction, and of the density of the slope across
        private double marginal, across;
    }
    // MicrosurfaceHeight Definition
    // (not in pbrt-v4) The distribution of the heights of the microsurface, for the random walk of Heitz, Hanika, d'Eon and
    // Dachsbacher, "Multiple-Scattering Microfacet BSDFs with the Smith Model" (SIGGRAPH 2016), as in the tutorial code of the
    // paper. The scattering of a Smith microsurface does not depend on it: the two are there to check that.
    public abstract class MicrosurfaceHeight
    {
        // The part of the heights below h, and its inverse
        public abstract Float C1(Float h);
        public abstract Float InvC1(Float u);
    }

    // Uniform in [-1, 1]
    public sealed class MicrosurfaceHeightUniform : MicrosurfaceHeight
    {
        public override Float C1(Float h)
        {
            return Clamp(0.5f * (h + 1), 0, 1);
        }

        public override Float InvC1(Float u)
        {
            return Clamp(2 * u - 1, -1, 1);
        }
    }

    // Gaussian, of mean 0 and variance 1
    public sealed class MicrosurfaceHeightGaussian : MicrosurfaceHeight
    {
        public override Float C1(Float h)
        {
            return (Float)(0.5 * Erfc(-h / Math.Sqrt(2.0)));
        }

        public override Float InvC1(Float u)
        {
            return Sqrt2 * ErfInv(2 * Clamp(u, 1e-7f, OneMinusEpsilon) - 1);
        }
    }

    // MicrosurfaceWalk Definition
    // (not in pbrt-v4) The steps of the random walk on a Smith microsurface that do not depend on what the microfacets are made
    // of (Heitz et al. 2016, section 4). A ray of the walk goes in any direction, and the distribution is asked for its single
    // sided cross section: the Lambda of a direction, of either sign of w.z, is Sigma(w) / w.z - 1.
    public static unsafe class MicrosurfaceWalk
    {
        public const Float Left = Float.MaxValue;

        public static Float Lambda(MicrofacetDistribution distribution, Vector3f w)
        {
            return distribution.Sigma(w) / w.z - 1;
        }

        // The height above which the walk starts
        public static Float StartHeight(MicrosurfaceHeight height)
        {
            return 1 + height.InvC1(0.999f);
        }

        // The probability that a ray leaving the height h upwards along w leaves the microsurface
        public static Float G1(MicrofacetDistribution distribution, MicrosurfaceHeight height, Vector3f w, Float h)
        {
            if (w.z > 0.9999f)
            {
                return 1;
            }
            if (w.z <= 0)
            {
                return 0;
            }
            return (Float)Math.Pow(height.C1(h), Lambda(distribution, w));
        }

        // The height of the next intersection of the ray from the height h along w, or Left
        public static Float SampleHeight(MicrofacetDistribution distribution, MicrosurfaceHeight height, Vector3f w, Float h, Float u)
        {
            if (w.z > 0.9999f)
            {
                return Left;
            }
            if (w.z < -0.9999f)
            {
                return height.InvC1(u * height.C1(h));
            }
            if (Math.Abs(w.z) < 0.0001f)
            {
                return h;
            }
            if (u > 1 - G1(distribution, height, w, h))
            {
                return Left;
            }
            return height.InvC1((Float)(height.C1(h) / Math.Pow(1 - u, 1 / Lambda(distribution, w))));
        }
    }
}
