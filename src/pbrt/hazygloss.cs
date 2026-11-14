// (not in pbrt-v4) hazygloss.cs
//
// The composite BRDF of Barla, Pacanowski and Vangorp, "A Composite BRDF Model for Hazy Gloss" (Computer Graphics Forum 37(4),
// EGSR 2018): a narrow and a wide specular lobe with one Fresnel term,
//     f = ((1 - beta) fn + beta fw) F                                                                          (Equation 1)
// and the mapping from what is seen (the reflectivity of the core rc, the haziness beta_h, the extent of the haze lambda_h) to
// what the model takes (the mixture ratio beta, the reflectivity r, the wide roughness): the haze is added around the core
// without a change of the core, whose peak stays rc fn(0). Ported from the HazyGlossMaterial and util/hazemap.h of the C++
// fork, which follow the paper and the BRDF Explorer shaders of its supplemental material (hazyGlossConductor.brdf,
// hazyGlossDielectric.brdf); where the fork goes beyond the paper it says so below.

using System;

namespace pbrt
{
    // HazeMapping Definition
    public static unsafe class HazeMapping
    {
        // Equation 8: the wide roughness along the tangent and the bitangent from the narrow roughness and the extents of the haze
        public static void WideRoughness(Float anx, Float any, Float lx, Float ly, out Float awx, out Float awy)
        {
            Float s = std.sqrt(anx * any);
            awx = anx + lx * s;
            awy = any + ly * s;
        }

        // Table 2: the peak of the wide lobe over the peak of the narrow one at normal incidence. For the distributions of
        // Beckmann (Cook-Torrance, Ward) and of Trowbridge and Reitz (Walter et al.) it is anx any / (awx awy); it is taken
        // here from the distributions themselves, D_w(n) / D_n(n), which is the same number and holds for the others.
        public static Float PeakRatio(MicrofacetDistribution narrow, MicrofacetDistribution wide)
        {
            Vector3f n = new Vector3f(0, 0, 1);
            Float dn = narrow.D(n);
            return dn > 0 ? std.min<Float>(1, wide.D(n) / dn) : 1;
        }

        // Equation 7: the intensity of the haze at normal incidence, between 0 and the largest that keeps the core positive
        // (rc <= p) or the energy conserved (rc > p)
        public static Float HazeIntensity(Float rc, Float p, Float betaH)
        {
            if (rc <= p)
            {
                return betaH * rc / p;
            }
            return betaH * (1 - rc) / (1 - p);
        }

        // Equations 12 to 15: the same without the corner at rc = p, on a rational quadratic Bezier curve with the weights
        // {1, w, 1} and the control points (0,0), (p, betaH), (1,0).
        public static Float HazeIntensityBezier(Float rc, Float p, Float betaH, Float w)
        {
            // The parameter u of the curve where its first coordinate is rc: A u^2 + B u - rc = 0, with A = 1 - 2wp - rc(2 - 2w)
            // and B = 2wp + rc(2 - 2w) (B is the b of Equation 14). The coordinate goes from 0 to 1 and does not turn back, so
            // one root is in [0,1]; the stable formulas for the roots stand for the Taylor expansion of the paper.
            double A = 1 - 2.0 * w * p - (double)rc * (2 - 2.0 * w);
            double B = 2.0 * w * p + (double)rc * (2 - 2.0 * w);
            double C = -(double)rc;
            double u;
            if (Math.Abs(A) < 1e-12)
            {
                u = -C / B;
            }
            else
            {
                double disc = Math.Max(0.0, B * B - 4 * A * C);
                double q = -0.5 * (B + (B >= 0 ? 1 : -1) * Math.Sqrt(disc));
                double u1 = q / A, u2 = (q != 0) ? C / q : 0.0;
                u = (u1 >= -1e-9 && u1 <= 1 + 1e-9) ? u1 : u2;
            }
            u = Math.Min(1.0, Math.Max(0.0, u));
            double den = (1 - u) * (1 - u) + 2 * (1 - u) * u * w + u * u;
            return (Float)(2 * (1 - u) * u * w * betaH / den);
        }

        // Gulbrandsen, "Artist Friendly Metallic Fresnel" (JCGT 3(4), 2014): the reflectivity r and the edge tint g to the
        // index of a conductor
        public static void GulbrandsenEtaK(Float r, Float g, out Float eta, out Float k)
        {
            // (in double: k is the root of a small difference of large terms when r is close to 1 or g to 0)
            double rr = Math.Min(Math.Max((double)r, 0.0), 0.99995);
            double s = Math.Sqrt(rr);
            double nmin = (1 - rr) / (1 + rr), nmax = (1 + s) / (1 - s);
            double n = g * nmin + (1 - (double)g) * nmax;
            double k2 = (rr * (n + 1) * (n + 1) - (n - 1) * (n - 1)) / (1 - rr);
            eta = (Float)n;
            // (without an edge tint the index is real: a dielectric, as in the shaders of the paper)
            k = g == 0 ? 0 : (Float)Math.Sqrt(Math.Max(k2, 0.0));
        }
    }

    // MixtureDistribution Definition
    // One microsurface whose normals are those of two distributions mixed, D = (1 - b) Dn + b Dw: the "intertwined" mixture of
    // the paper (section 3), in which a facet of one is shadowed by facets of both. Lambda is the integral of the slopes above
    // a direction and is linear in the distribution, as is the projected area, so both are mixed the same way; the visible
    // normals are sampled exactly, a distribution chosen by its share of the projected area.
    public sealed unsafe class MixtureDistribution : MicrofacetDistribution
    {
        public MixtureDistribution(MicrofacetDistribution narrow, MicrofacetDistribution wide, Float b)
        {
            this.narrow = narrow;
            this.wide = wide;
            this.b = Clamp(b, 0, 1);
        }

        public override Float D(Vector3f wm)
        {
            return (1 - b) * narrow.D(wm) + b * wide.D(wm);
        }

        public override Float Lambda(Vector3f w)
        {
            return (1 - b) * narrow.Lambda(w) + b * wide.Lambda(w);
        }

        public override Float Sigma(Vector3f w)
        {
            return (1 - b) * narrow.Sigma(w) + b * wide.Sigma(w);
        }

        public override Vector3f Sample_wm_SingleSided(Vector3f w, Point2f u)
        {
            Float sn = (1 - b) * narrow.Sigma(w), sw = b * wide.Sigma(w);
            Float sum = sn + sw;
            if (!(sum > 0))
            {
                return new Vector3f(0, 0, 1);
            }
            Float pn = sn / sum;
            if (u[0] < pn)
            {
                return narrow.Sample_wm_SingleSided(w, new Point2f(std.min<Float>(u[0] / pn, OneMinusEpsilon), u[1]));
            }
            return wide.Sample_wm_SingleSided(w, new Point2f(std.min<Float>((u[0] - pn) / (1 - pn), OneMinusEpsilon), u[1]));
        }

        public override bool EffectivelySmooth()
        {
            return (b == 1 || narrow.EffectivelySmooth()) && (b == 0 || wide.EffectivelySmooth());
        }

        public override void Regularize()
        {
            narrow.Regularize();
            wide.Regularize();
        }

        // (the mixture has no roughness of its own: the root of the mean of the squares, which is that of the mean slope variance)
        public override Float AlphaX()
        {
            return std.sqrt((1 - b) * Sqr(narrow.AlphaX()) + b * Sqr(wide.AlphaX()));
        }

        public override Float AlphaY()
        {
            return std.sqrt((1 - b) * Sqr(narrow.AlphaY()) + b * Sqr(wide.AlphaY()));
        }

        // (the V-cavity masking is written for the slopes of one distribution)
        public override double UnitSlopeCdf(double x) { throw new NotSupportedException("MixtureDistribution has no V-cavity form"); }
        public override double UnitSlopeDensity(double x) { throw new NotSupportedException("MixtureDistribution has no V-cavity form"); }
        public override double UnitSlopeMoment(double x) { throw new NotSupportedException("MixtureDistribution has no V-cavity form"); }
        public override double SampleUnitSlopeAcross(double x, double u) { throw new NotSupportedException("MixtureDistribution has no V-cavity form"); }

        public override string ToString()
        {
            return StringPrintf("[ MixtureDistribution b: %f narrow: %s wide: %s ]", b, narrow, wide);
        }

        private MicrofacetDistribution narrow, wide;
        private Float b;
    }

    // HazyGlossBxDF Definition
    // The composite of a narrow and a wide lobe that share their Fresnel term, f = (1 - beta) fn + beta fw, a lobe sampled with
    // its share (section 4.2 of the paper: no new sampling is needed). The lobes are any two BxDFs. A Lambertian base under
    // them is of the fork, not of the paper.
    public sealed unsafe class HazyGlossBxDF : BxDF
    {
        public HazyGlossBxDF(BxDF narrow, BxDF wide, Float beta)
        {
            this.narrow = narrow;
            this.wide = wide;
            this.beta = Clamp(beta, 0, 1);
        }

        public static string Name() { return "HazyGlossBxDF"; }

        public Float Beta() { return beta; }

        public override string ToString()
        {
            return StringPrintf("[ HazyGlossBxDF beta: %f diffuse prob: %f narrow: %s wide: %s ]", beta, pd, narrow, wide);
        }

        // The Lambertian base, of reflectance Kd, and the probability of sampling it
        public void SetDiffuseBase(SampledSpectrum K, Float probability)
        {
            Kd = K;
            pd = probability;
            coupledEta = 0;
        }

        // The base under a flat dielectric interface of index eta, with all the reflections between the two summed:
        //     f_d = Kd (1 - F(o)) (1 - F(i)) / (pi eta^2 (1 - Kd Fdr_int)),
        // Fdr_int = 2 int_0^1 F_in(mu) mu dmu the diffuse reflectance of the interface from inside. It conserves energy and is
        // reciprocal; it is exact for a smooth interface.
        public void SetCoupledDiffuseBase(SampledSpectrum K, Float probability, Float eta)
        {
            Kd = K;
            pd = probability;
            coupledEta = eta;
            fdrInt = InternalDiffuseReflectance(eta);
        }

        public static Float InternalDiffuseReflectance(Float eta)
        {
            const int N = 512;
            double sum = 0;
            for (int i = 0; i < N; ++i)
            {
                double mu = (i + 0.5) / N;
                sum += FrDielectric((Float)mu, 1 / eta) * mu;
            }
            return (Float)(2 * sum / N);
        }

        public override BxDFFlags Flags()
        {
            BxDFFlags fl = BxDFFlags.Unset;
            if (beta < 1)
            {
                fl |= narrow.Flags();
            }
            if (beta > 0)
            {
                fl |= wide.Flags();
            }
            if (pd > 0)
            {
                fl |= BxDFFlags.DiffuseReflection;
            }
            return fl;
        }

        public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            SampledSpectrum fv = Spec_f(wo, wi, mode);
            if (pd > 0 && SameHemisphere(wo, wi))
            {
                fv += DiffuseF(wo, wi);
            }
            return fv;
        }

        public override pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            bool diffuse = pd > 0 && (sampleFlags & BxDFReflTransFlags.Reflection) != 0;
            if (diffuse)
            {
                if (uc < pd)
                {
                    // The Lambertian base
                    if (wo.z == 0)
                    {
                        return default;
                    }
                    Vector3f wi = SampleCosineHemisphere(u);
                    if (wo.z < 0)
                    {
                        wi.z *= -1;
                    }
                    Float pdfBase = PDF(wo, wi, mode, sampleFlags);
                    SampledSpectrum fBase = f(wo, wi, mode);
                    if (pdfBase == 0 || !(bool)fBase)
                    {
                        return default;
                    }
                    return new BSDFSample(fBase, wi, pdfBase, BxDFFlags.DiffuseReflection);
                }
                uc = std.min<Float>((uc - pd) / (1 - pd), OneMinusEpsilon);
            }
            Float ps = 1 - (diffuse ? pd : 0);
            bool chooseNarrow = uc < 1 - beta;
            Float wc = chooseNarrow ? 1 - beta : beta;
            Float ucRemap = chooseNarrow ? uc / (1 - beta) : (uc - (1 - beta)) / beta;
            ucRemap = std.min<Float>(ucRemap, OneMinusEpsilon);
            pstd.optional<BSDFSample> bs = (chooseNarrow ? narrow : wide).Sample_f(wo, ucRemap, u, mode, sampleFlags);
            if (!bs || !(bool)bs.value().f || bs.value().pdf == 0)
            {
                return default;
            }
            BSDFSample s = bs.value();
            // A specular sample, or one of a lobe whose f is an estimate (its pdf only proportional): f / pdf is the weight of
            // the lobe that was chosen, and both are scaled by the probability of the choice
            if (s.IsSpecular() || s.pdfIsProportional)
            {
                s.f *= wc;
                s.pdf *= wc * ps;
                return s;
            }
            // Rough lobes: the f and the pdf of the mixture in the direction
            Float pdf = PDF(wo, s.wi, mode, sampleFlags);
            SampledSpectrum fv = f(wo, s.wi, mode);
            if (pdf == 0 || !(bool)fv)
            {
                return default;
            }
            return new BSDFSample(fv, s.wi, pdf, s.flags, s.eta, false);
        }

        public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            Float pn = beta < 1 ? narrow.PDF(wo, wi, mode, sampleFlags) : 0;
            Float pw = beta > 0 ? wide.PDF(wo, wi, mode, sampleFlags) : 0;
            Float spec = (1 - beta) * pn + beta * pw;
            if (!(pd > 0 && (sampleFlags & BxDFReflTransFlags.Reflection) != 0))
            {
                return spec;
            }
            Float diff = SameHemisphere(wo, wi) ? CosineHemispherePDF(AbsCosTheta(wi)) : 0;
            return pd * diff + (1 - pd) * spec;
        }

        public override void Regularize()
        {
            narrow.Regularize();
            wide.Regularize();
        }

        // Each lobe keeps its Mueller matrix for its share of the light; the base depolarises
        public override bool HasMuellerMatrix() { return true; }

        public override SampledSpectrum f(BSDF bsdf, Vector3f wo, Vector3f wi, TransportMode mode, out bool polarised,
                                          out PolarisedLightSystem.Attenuation matrix)
        {
            polarised = false;
            matrix = default;
            SampledSpectrum value = f(wo, wi, mode);
            if (!(bool)value)
            {
                return value;
            }
            bool fromCamera = mode == TransportMode.Radiance;
            Vector3f woRender = bsdf.LocalToRender(wo), wiRender = bsdf.LocalToRender(wi);
            Vector3f directionExit = fromCamera ? woRender : wiRender;
            Vector3f directionEnter = fromCamera ? -wiRender : -woRender;
            PolarisedLightSystem.Attenuation sum = default;
            bool hasSum = false;
            SampledSpectrum fn = beta < 1 ? (1 - beta) * narrow.f(wo, wi, mode) : new SampledSpectrum(0f);
            SampledSpectrum fw = beta > 0 ? beta * wide.f(wo, wi, mode) : new SampledSpectrum(0f);
            if ((bool)fn)
            {
                Accumulate(ref sum, ref hasSum, LobeMatrix(bsdf, narrow, woRender, wiRender, mode), fn, directionExit, directionEnter);
            }
            if ((bool)fw)
            {
                Accumulate(ref sum, ref hasSum, LobeMatrix(bsdf, wide, woRender, wiRender, mode), fw, directionExit, directionEnter);
            }
            if (pd > 0 && SameHemisphere(wo, wi))
            {
                SampledSpectrum fd = DiffuseF(wo, wi);
                if ((bool)fd)
                {
                    Accumulate(ref sum, ref hasSum, PolarisedLightSystem.Attenuation.Lambert(new SampledSpectrum(1f)), fd, directionExit, directionEnter);
                }
            }
            if (hasSum)
            {
                polarised = PolarisedFacet.Normalise(sum, value, new SampledSpectrum(1f), out matrix);
            }
            return value;
        }

        public override pstd.optional<BSDFSample> Sample_f(BSDF bsdf, Vector3f wo, Float uc, Point2f u, TransportMode mode,
                                                           BxDFReflTransFlags sampleFlags, out bool polarised,
                                                           out PolarisedLightSystem.Attenuation matrix)
        {
            polarised = false;
            matrix = default;
            pstd.optional<BSDFSample> bs = Sample_f(wo, uc, u, mode, sampleFlags);
            if (bs && !bs.value().IsSpecular())
            {
                f(bsdf, wo, bs.value().wi, mode, out polarised, out matrix);
            }
            return bs;
        }

        private static PolarisedLightSystem.Attenuation LobeMatrix(BSDF bsdf, BxDF lobe, Vector3f woRender, Vector3f wiRender, TransportMode mode)
        {
            PolarisedLightSystem.Attenuation own;
            if (!PolarisedPathIntegrator.RoughAttenuation(bsdf.WithBxDF(lobe), null, woRender, wiRender, new SampledSpectrum(1f), mode, out own))
            {
                own = PolarisedLightSystem.Attenuation.Lambert(new SampledSpectrum(1f));
            }
            return own;
        }

        private static void Accumulate(ref PolarisedLightSystem.Attenuation sum, ref bool hasSum, PolarisedLightSystem.Attenuation matrix,
                                       SampledSpectrum contribution, Vector3f directionExit, Vector3f directionEnter)
        {
            matrix *= contribution;
            if (!matrix.WithFrames(directionExit, directionEnter) || matrix.HasNaNs())
            {
                return;
            }
            if (hasSum)
            {
                sum += matrix;
            }
            else
            {
                sum = matrix;
                hasSum = true;
            }
        }

        private SampledSpectrum DiffuseF(Vector3f wo, Vector3f wi)
        {
            if (coupledEta <= 0)
            {
                return Kd * InvPi;
            }
            Float to = 1 - FrDielectric(AbsCosTheta(wo), coupledEta);
            Float ti = 1 - FrDielectric(AbsCosTheta(wi), coupledEta);
            SampledSpectrum den = new SampledSpectrum(1f) - Kd * fdrInt;
            SampledSpectrum r = Kd * (to * ti * InvPi / (coupledEta * coupledEta));
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                r[i] = den[i] > 0 ? r[i] / den[i] : 0;
            }
            return r;
        }

        private SampledSpectrum Spec_f(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            SampledSpectrum fn = beta < 1 ? narrow.f(wo, wi, mode) : new SampledSpectrum(0f);
            SampledSpectrum fw = beta > 0 ? wide.f(wo, wi, mode) : new SampledSpectrum(0f);
            return (1 - beta) * fn + beta * fw;
        }

        // HazyGlossBxDF Private Members
        private BxDF narrow, wide;
        private Float beta;
        private SampledSpectrum Kd = new SampledSpectrum(0f);
        private Float pd = 0;
        private Float coupledEta = 0, fdrInt = 0;
    }

    // HazyDielectricBxDF Definition
    // (ours, not of the paper) The composite of the paper for a dielectric, with the two uses of its index apart. The paper's
    // mapping raises the reflectivity to make room for the halo, and with it the index, so that what is seen through the glass
    // moves as the haziness is turned (its Figure 13). Here the directions of the refraction (and the total internal
    // reflection) follow etaGeometry, which stays, and the amount reflected by a facet is the Fresnel reflectance of etaFresnel
    // for the angle on the outer side of the facet. That angle is the same for a path and its reverse, so the model is as
    // reciprocal as the lobes of pbrt are; and a facet reflects F and transmits 1 - F whatever F is, so the energy is conserved
    // as in those lobes. The reflection and the transmission have the same normals, D = (1 - beta) Dn + beta Dw as large
    // patches: with normals of their own they do not conserve energy (a narrow reflection that is total inside the glass and
    // a wide transmission that is not add up to more than one).
    public sealed unsafe class HazyDielectricBxDF : BxDF
    {
        public HazyDielectricBxDF() { }

        public HazyDielectricBxDF(Float etaGeometry, Float etaFresnel, MicrofacetDistribution narrow, MicrofacetDistribution wide, Float beta)
        {
            this.etaGeometry = etaGeometry;
            this.etaFresnel = etaFresnel;
            this.narrow = narrow;
            this.wide = wide;
            this.beta = Clamp(beta, 0, 1);
        }

        public static string Name() { return "HazyDielectricBxDF"; }

        public Float EtaGeometry() { return etaGeometry; }
        public Float EtaFresnel() { return etaFresnel; }

        public override string ToString()
        {
            return StringPrintf("[ HazyDielectricBxDF etaGeometry: %f etaFresnel: %f beta: %f ]", etaGeometry, etaFresnel, beta);
        }

        public override BxDFFlags Flags()
        {
            return BxDFFlags.GlossyReflection | BxDFFlags.GlossyTransmission;
        }

        public override void Regularize()
        {
            narrow.Regularize();
            wide.Regularize();
        }

        // The reflectance of a facet with the normal wm for the direction w: Fresnel's, of etaFresnel, at the angle on the outer
        // side of the facet (from inside, the angle of the refracted ray by etaGeometry; none: total internal reflection)
        public Float FacetReflectance(Vector3f w, Vector3f wm)
        {
            Float cosine = AbsDot(w, wm);
            if (w.z < 0)
            {
                Float sin2 = Sqr(etaGeometry) * std.max<Float>(0, 1 - Sqr(cosine));
                if (sin2 >= 1)
                {
                    return 1;
                }
                cosine = SafeSqrt(1 - sin2);
            }
            return FrDielectric(cosine, etaFresnel);
        }

        // The half vector of pbrt's DielectricBxDF::f(), false if there is none
        private bool HalfVector(Vector3f wo, Vector3f wi, out Vector3f wm, out Float etap, out bool reflect)
        {
            Float cosTheta_o = CosTheta(wo), cosTheta_i = CosTheta(wi);
            reflect = cosTheta_i * cosTheta_o > 0;
            etap = 1;
            if (!reflect)
            {
                etap = cosTheta_o > 0 ? etaGeometry : (1 / etaGeometry);
            }
            wm = wi * etap + wo;
            if (cosTheta_i == 0 || cosTheta_o == 0 || LengthSquared(wm) == 0)
            {
                return false;
            }
            wm = FaceForward(Normalize(wm), new Normal3f(0, 0, 1));
            // Discard backfacing microfacets
            if (Dot(wm, wi) * cosTheta_i < 0 || Dot(wm, wo) * cosTheta_o < 0)
            {
                return false;
            }
            return true;
        }

        public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            Vector3f wm;
            Float etap;
            bool reflect;
            if (!HalfVector(wo, wi, out wm, out etap, out reflect))
            {
                return new SampledSpectrum(0f);
            }
            Float F = FacetReflectance(wo, wm);
            Float cosTheta_o = CosTheta(wo), cosTheta_i = CosTheta(wi);
            Float lobes = (1 - beta) * narrow.D(wm) * narrow.G(wo, wi) + (beta > 0 ? beta * wide.D(wm) * wide.G(wo, wi) : 0);
            if (reflect)
            {
                Float fr = lobes * F / std.abs(4 * cosTheta_i * cosTheta_o);
                return new SampledSpectrum(IsFinite(fr) ? fr : 0);
            }
            Float denom = Sqr(Dot(wi, wm) + Dot(wo, wm) / etap) * cosTheta_i * cosTheta_o;
            Float ft = lobes * (1 - F) * std.abs(Dot(wi, wm) * Dot(wo, wm) / denom);
            // (a half vector along which the two directions cancel: no facet)
            if (!IsFinite(ft))
            {
                return new SampledSpectrum(0f);
            }
            // Account for non-symmetry with transmission to different medium
            if (mode == TransportMode.Radiance)
            {
                ft /= Sqr(etap);
            }
            return new SampledSpectrum(ft);
        }

        // The probability of sampling the reflection: the reflectance of the flat surface, kept away from 0 and 1
        private bool Selection(Vector3f wo, BxDFReflTransFlags sampleFlags, out Float pR, out Float pT)
        {
            Float a = Clamp(FacetReflectance(wo, new Vector3f(0, 0, 1)), 0.1f, 0.9f);
            pR = (sampleFlags & BxDFReflTransFlags.Reflection) != 0 ? a : 0;
            pT = (sampleFlags & BxDFReflTransFlags.Transmission) != 0 ? 1 - a : 0;
            Float sum = pR + pT;
            if (sum == 0)
            {
                return false;
            }
            pR /= sum;
            pT /= sum;
            return true;
        }

        public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            Float pR, pT;
            Vector3f wm;
            Float etap;
            bool reflect;
            if (!Selection(wo, sampleFlags, out pR, out pT) || !HalfVector(wo, wi, out wm, out etap, out reflect))
            {
                return 0;
            }
            Float normals = (1 - beta) * narrow.PDF(wo, wm) + (beta > 0 ? beta * wide.PDF(wo, wm) : 0);
            Float pdf;
            if (reflect)
            {
                pdf = pR * normals / (4 * AbsDot(wo, wm));
            }
            else
            {
                Float dwm_dwi = AbsDot(wi, wm) / Sqr(Dot(wi, wm) + Dot(wo, wm) / etap);
                pdf = pT * normals * dwm_dwi;
            }
            return IsFinite(pdf) ? pdf : 0;
        }

        public override pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            Float pR, pT;
            if (wo.z == 0 || !Selection(wo, sampleFlags, out pR, out pT))
            {
                return default;
            }
            Vector3f wi;
            Float etap = 1;
            BxDFFlags flags;
            if (uc < pR)
            {
                Float choice = uc / pR;
                Vector3f wm = (choice < 1 - beta ? narrow : wide).Sample_wm(wo, u);
                wi = Reflect(wo, wm);
                if (!SameHemisphere(wo, wi))
                {
                    return default;
                }
                flags = BxDFFlags.GlossyReflection;
            }
            else
            {
                Float choice = (uc - pR) / pT;
                Vector3f wm = (choice < 1 - beta ? narrow : wide).Sample_wm(wo, u);
                bool tir = !Refract(wo, (Normal3f)wm, etaGeometry, &etap, &wi);
                if (tir || SameHemisphere(wo, wi) || wi.z == 0)
                {
                    return default;
                }
                flags = BxDFFlags.GlossyTransmission;
            }
            Float pdf = PDF(wo, wi, mode, sampleFlags);
            SampledSpectrum value = f(wo, wi, mode);
            if (pdf == 0 || !(bool)value)
            {
                return default;
            }
            return new BSDFSample(value, wi, pdf, flags, etap, false);
        }

        // HazyDielectricBxDF Private Members
        private Float etaGeometry = 1.5f, etaFresnel = 1.5f;
        private MicrofacetDistribution narrow = new TrowbridgeReitzDistribution(0.1f, 0.1f), wide = new TrowbridgeReitzDistribution(0.1f, 0.1f);
        private Float beta;
    }

    // HazyTransmissionBxDF Definition
    // (ours) A haze of the transmitted light alone: under the interface (a HazyDielectricBxDF), a thin layer that scatters
    // forward and absorbs nothing, of the optical thickness tau; the light leaves the layer into the glass without another
    // interface. Of the light that goes straight through, 1 - exp(-tau) is scattered into a halo (the phase function of
    // Henyey and Greenstein with g close to 1), which is the haze of a sheet as it is measured. The reflection of the interface
    // is as it was but for the little that the layer sends back. This is the random walk of LayeredBxDF, so the energy is
    // conserved by construction; its f() does not count the light that crosses a rough interface and leaves through the
    // other, flat one without a scattering (pbrt connects to a flat exit only from a scattering), and that term is added here.
    public sealed unsafe class HazyTransmissionBxDF : LayeredBxDF<HazyDielectricBxDF, DielectricBxDF>
    {
        public HazyTransmissionBxDF() : base(false) { }

        public HazyTransmissionBxDF(HazyDielectricBxDF top, Float tau, Float g, int maxDepth, int nSamples)
            : base(top, new DielectricBxDF(1, new TrowbridgeReitzDistribution(0, 0)), tau, new SampledSpectrum(1f), g, maxDepth, nSamples, false)
        {
            this.tau = tau;
            this.samples = nSamples;
        }

        public static string Name() { return "HazyTransmissionBxDF"; }

        // The share of the transmitted light that the layer scatters at normal incidence, to its optical thickness
        public static Float OpticalThickness(Float haziness)
        {
            return -std.log(std.max<Float>(1 - Clamp(haziness, 0, 0.999f), 1e-6f));
        }

        // (the Mueller matrices of the layers are written for the interfaces of pbrt)
        public override bool HasMuellerMatrix() { return false; }

        // What crosses the interface and the layer without a scattering in it: the interface, times what the layer lets through
        // along the direction on the side of the glass
        private Float Straight(Vector3f wo, Vector3f wi)
        {
            if (SameHemisphere(wo, wi) || wo.z == 0 || wi.z == 0)
            {
                return 0;
            }
            Vector3f inside = wo.z < 0 ? wo : wi;
            return std.exp(-tau / std.abs(inside.z));
        }

        public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            SampledSpectrum value = base.f(wo, wi, mode);
            Float straight = Straight(wo, wi);
            if (straight > 0)
            {
                value += straight * top.f(wo, wi, mode);
            }
            return value;
        }

        public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            Float pdf = base.PDF(wo, wi, mode, sampleFlags);
            Float straight = Straight(wo, wi);
            if (straight > 0)
            {
                pdf += straight * top.PDF(wo, wi, mode, sampleFlags);
            }
            return pdf;
        }

        // The walk multiplies the values and the densities of its interface events apart from each other; under a nearly
        // smooth interface each is large, and a long walk can take both past what a float holds. Such a sample is dropped.
        public override pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            pstd.optional<BSDFSample> bs = base.Sample_f(wo, uc, u, mode, sampleFlags);
            if (bs && (!IsFinite(bs.value().pdf) || !IsFinite(bs.value().f[0]) || bs.value().f.HasNaNs()))
            {
                return default;
            }
            return bs;
        }

        private Float tau;
        private int samples;
    }

    public enum HazyGlossMode
    {
        Perceptual,
        Physical
    }

    public enum HazyGlossKind
    {
        Conductor,
        Dielectric,
        Plastic
    }

    // What the mapping gives at a point: Equation 1 is evaluated with these
    public struct HazyGlossParameters
    {
        // The narrow and the wide roughness
        public Float anx, any, awx, awy;
        // The mixture ratio
        public Float beta;
        // The index of a conductor, of a dielectric or plastic
        public SampledSpectrum eta, k;
        public Float dielectricEta;
        // The index of the core alone, before the haze raises the reflectivity (perceptual mode; else dielectricEta)
        public Float coreEta;
    }

    // HazyGlossMaterial Definition
    // Material "hazygloss". The roughnesses are the alpha of the distribution unless "remaproughness" is true.
    //   string mode         "perceptual" (the default): the reflectivity of the core, the haziness and the extent of the haze are
    //                       mapped to the parameters of the model (the Algorithm of the paper); "physical": Equation 1 with its
    //                       own parameters
    //   string kind         "conductor" (the default), "dielectric" (reflection and refraction) or "plastic" (the reflection of
    //                       a dielectric, over an optional "base.reflectance")
    //   float roughness, uroughness, vroughness     the narrow lobe; 0.02
    //   perceptual:  spectrum reflectance    the reflectivity of the core, rc; 0.5
    //                spectrum edgetint       the edge tint g of a conductor; 1
    //                float haziness          beta_h in [0,1); 0
    //                float hazeextent, hazeextent.u, hazeextent.v      lambda_h; 5
    //                float smoothing         the weight w of the Bezier curve (0: the piecewise linear Equation 7)
    //   physical:    float beta, float wideroughness, wideuroughness, widevroughness (0.2);
    //                conductor: spectrum eta and k (copper); dielectric and plastic: ior (1.5; also "sellmeier", "cauchy", "cauchyb")
    //   string distribution  "ggx" (the default), "beckmann" or "studentt" with "float gamma" (Table 2 of the paper; the peak
    //                       ratio is taken from the distributions)
    //   string masking      "separate" (the default: large patches of the two surfaces, each lobe with its own masking) or
    //                       "intertwined" (one surface with D = (1 - b) Dn + b Dw, and the masking of the mixture)
    //   integer scatterdepth  the orders of scattering among the microfacets (1: single scattering, as in the paper; more: the
    //                       random walk of the multiple-scattering microsurfaces), for the conductor and the dielectric
    //   dielectric, ours:
    //     ior (perceptual mode)          the index of the refraction, which the haze then does not change (HazyDielectricBxDF)
    //     float transmissionhaziness     the share of the transmitted light that is scattered into a halo of its own, in [0,1),
    //                                    by a forward scattering layer under the interface (HazyTransmissionBxDF); the index
    //                                    of the refraction is then that of the core reflectivity unless "ior" gives it
    //     float transmissionhazeg        the g of the phase function of that layer (closer to 1: a narrower halo); 0.9
    //     integer maxdepth, nsamples     of the random walk in the layer; 100, 1
    //   bool base.coupled   plastic: the base under the interface with the reflections between them (energy conserving)
    //                       instead of added to the lobes
    public sealed unsafe class HazyGlossMaterial : Material
    {
        public HazyGlossMode mode;
        public HazyGlossKind kind;
        public FloatTexture uRoughness, vRoughness, haziness, hazeU, hazeV, beta, wideU, wideV, displacement;
        // (ours) The haze of the transmission of a dielectric: the share of the transmitted light that a layer under the
        // interface scatters at normal incidence, and the g of its phase function
        public FloatTexture transmissionHaziness;
        public Float transmissionHazeG = 0.9f;
        // (the walk in the layer counts every scattering: light that meets the interface from inside at a grazing angle crosses
        // the layer twice and scatters many times, and a short walk drops it: a third of the energy with 10 events)
        public int layerDepth = 100, layerSamples = 1;
        public SpectrumTexture reflectance, edgeTint, eta, k, baseReflectance;
        public Spectrum dielectricEta;
        public Float bezierW;
        public Image normalMap;
        public bool remapRoughness, intertwined, coupledBase;
        public int scatterDepth = 1;
        public MicrofacetDistributionType distribution = MicrofacetDistributionType.TrowbridgeReitz;
        public Float gamma = 2;

        public static string Name() { return "HazyGlossMaterial"; }

        public override FloatTexture GetDisplacement() { return displacement; }
        public override Image GetNormalMap() { return normalMap; }
        public override bool HasSubsurfaceScattering() { return false; }

        public override BSSRDF GetBSSRDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx, ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            return null;
        }

        public override bool CanEvaluateTextures<TextureEvaluator>(TextureEvaluator texEval)
        {
            return texEval.CanEvaluate(new FloatTexture[] { uRoughness, vRoughness, haziness, hazeU, hazeV, beta, wideU, wideV },
                                       new SpectrumTexture[] { reflectance, edgeTint, eta, k, baseReflectance });
        }

        public override string ToString()
        {
            return StringPrintf("[ HazyGlossMaterial mode: %s kind: %s ]", mode.ToString(), kind.ToString());
        }

        // The Algorithm of the paper (HazeMapping), or the parameters as they are given
        public HazyGlossParameters Parameters<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx, ref SampledWavelengths lambda)
            where TextureEvaluator : ITextureEvaluator
        {
            HazyGlossParameters q = new HazyGlossParameters();
            q.eta = new SampledSpectrum(1f);
            q.k = new SampledSpectrum(0f);
            q.dielectricEta = 1.5f;
            Float anx = texEval.Apply(uRoughness, ctx), any = texEval.Apply(vRoughness, ctx);
            if (remapRoughness)
            {
                anx = TrowbridgeReitzDistribution.RoughnessToAlpha(anx);
                any = TrowbridgeReitzDistribution.RoughnessToAlpha(any);
            }
            anx = std.max<Float>(anx, 1e-4f);
            any = std.max<Float>(any, 1e-4f);
            Float awx, awy, b;
            if (mode == HazyGlossMode.Perceptual)
            {
                SampledSpectrum rc = pbrt.Clamp(texEval.Apply(reflectance, ctx, lambda), 0, 1);
                // The largest coefficient of the reflectivity of the core over fixed wavelengths, so that the mixture ratio does
                // not depend on the wavelengths of the ray
                Float rcHat = 0;
                for (int i = 0; i < 6; ++i)
                {
                    SampledWavelengths fixedL = SampledWavelengths.SampleVisible((Float)i / 24);
                    rcHat = std.max(rcHat, pbrt.Clamp(texEval.Apply(reflectance, ctx, fixedL), 0, 1).MaxComponentValue());
                }
                Float lx = std.max<Float>(texEval.Apply(hazeU, ctx), 0), ly = std.max<Float>(texEval.Apply(hazeV, ctx), 0);
                Float bh = Clamp(texEval.Apply(haziness, ctx), 0, 0.9999f);
                HazeMapping.WideRoughness(anx, any, lx, ly, out awx, out awy);
                Float p = HazeMapping.PeakRatio(MicrofacetDistribution.Create(distribution, anx, any, gamma), MicrofacetDistribution.Create(distribution, awx, awy, gamma));
                Float kh = 0;
                if (rcHat > 0)
                {
                    kh = bezierW > 0 ? HazeMapping.HazeIntensityBezier(rcHat, p, bh, bezierW) : HazeMapping.HazeIntensity(rcHat, p, bh);
                }
                Float rHat = rcHat + (1 - p) * kh;              // Equation 5
                b = rHat > 0 ? kh / rHat : 0;                   // Equation 4
                Float scale = rcHat > 0 ? rHat / rcHat : 1;
                SampledSpectrum rS = rc * scale;                // r = (rHat / rcHat) rc: the haze has the colour of the core
                if (kind == HazyGlossKind.Conductor)
                {
                    SampledSpectrum g = pbrt.Clamp(texEval.Apply(edgeTint, ctx, lambda), 0, 1);
                    for (int i = 0; i < NSpectrumSamples; ++i)
                    {
                        Float e, kk;
                        HazeMapping.GulbrandsenEtaK(rS[i], g[i], out e, out kk);
                        q.eta[i] = e;
                        q.k[i] = kk;
                    }
                }
                else
                {
                    Float sr = std.sqrt(std.min<Float>(rHat, 0.99995f));
                    q.dielectricEta = (1 + sr) / (1 - sr);
                    Float sc = std.sqrt(std.min<Float>(rcHat, 0.99995f));
                    q.coreEta = (1 + sc) / (1 - sc);
                }
            }
            else
            {
                awx = texEval.Apply(wideU, ctx);
                awy = texEval.Apply(wideV, ctx);
                if (remapRoughness)
                {
                    awx = TrowbridgeReitzDistribution.RoughnessToAlpha(awx);
                    awy = TrowbridgeReitzDistribution.RoughnessToAlpha(awy);
                }
                awx = std.max<Float>(awx, 1e-4f);
                awy = std.max<Float>(awy, 1e-4f);
                b = Clamp(texEval.Apply(beta, ctx), 0, 1);
                if (kind == HazyGlossKind.Conductor)
                {
                    q.eta = texEval.Apply(eta, ctx, lambda);
                    q.k = texEval.Apply(k, ctx, lambda);
                }
                else
                {
                    q.dielectricEta = ParametricSpectra.SampleIndex(dielectricEta, null, texEval, ctx, ref lambda);
                    q.coreEta = q.dielectricEta;
                }
            }
            q.anx = anx; q.any = any; q.awx = awx; q.awy = awy;
            q.beta = b;
            return q;
        }

        public HazyGlossBxDF GetBxDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx, ref SampledWavelengths lambda)
            where TextureEvaluator : ITextureEvaluator
        {
            HazyGlossParameters q = Parameters(texEval, ctx, ref lambda);
            Float b = q.beta;
            MicrofacetDistribution dn = MicrofacetDistribution.Create(distribution, q.anx, q.any, gamma);
            MicrofacetDistribution dw = MicrofacetDistribution.Create(distribution, q.awx, q.awy, gamma);
            if (intertwined)
            {
                // One microsurface with the mixture of the two distributions: the composite is then a single lobe
                dn = new MixtureDistribution(dn, dw, b);
                dw = dn;
                b = 0;
            }
            if (kind == HazyGlossKind.Conductor)
            {
                if (scatterDepth > 1)
                {
                    MicrosurfaceHeight height = new MicrosurfaceHeightUniform();
                    return new HazyGlossBxDF(new MultiScatterConductorBxDF(dn, height, q.eta, q.k, scatterDepth),
                                             new MultiScatterConductorBxDF(dw, height, q.eta, q.k, scatterDepth), b);
                }
                return new HazyGlossBxDF(new ConductorBxDF(dn, q.eta, q.k), new ConductorBxDF(dw, q.eta, q.k), b);
            }
            if (kind == HazyGlossKind.Dielectric)
            {
                if (scatterDepth > 1)
                {
                    MicrosurfaceHeight height = new MicrosurfaceHeightUniform();
                    return new HazyGlossBxDF(new MultiScatterDielectricBxDF(dn, height, q.dielectricEta, scatterDepth),
                                             new MultiScatterDielectricBxDF(dw, height, q.dielectricEta, scatterDepth), b);
                }
                return new HazyGlossBxDF(new DielectricBxDF(q.dielectricEta, dn), new DielectricBxDF(q.dielectricEta, dw), b);
            }
            // Plastic: the reflection of a dielectric alone (a conductor without absorption has its Fresnel term), over an
            // optional Lambertian base
            SampledSpectrum etaP = new SampledSpectrum(q.dielectricEta), kP = new SampledSpectrum(0f);
            HazyGlossBxDF bxdf = new HazyGlossBxDF(new ConductorBxDF(dn, etaP, kP), new ConductorBxDF(dw, etaP, kP), b);
            if (baseReflectance != null)
            {
                SampledSpectrum Kd = pbrt.Clamp(texEval.Apply(baseReflectance, ctx, lambda), 0, 1);
                Float F0 = Sqr((q.dielectricEta - 1) / (q.dielectricEta + 1));
                Float kmax = Kd.MaxComponentValue();
                Float pdiff = kmax > 0 ? kmax / (kmax + std.max<Float>(F0, 0.04f)) : 0;
                if (coupledBase)
                {
                    bxdf.SetCoupledDiffuseBase(Kd, pdiff, q.dielectricEta);
                }
                else
                {
                    bxdf.SetDiffuseBase(Kd, pdiff);
                }
            }
            return bxdf;
        }

        // Whether the dielectric has a haze of its own for the transmission ("transmissionhaziness") or an index that the
        // haze does not change ("ior" in the perceptual mode): HazyDielectricBxDF, under HazyTransmissionBxDF for the former
        public bool SeparateTransmission()
        {
            return transmissionHaziness != null || (mode == HazyGlossMode.Perceptual && dielectricEta != null);
        }

        public HazyDielectricBxDF InterfaceBxDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx, ref SampledWavelengths lambda,
                                                                  HazyGlossParameters q)
            where TextureEvaluator : ITextureEvaluator
        {
            // The index of the refraction: the one given, or that of the core without its haze
            Float etaGeometry = q.coreEta;
            if (mode == HazyGlossMode.Perceptual && dielectricEta != null)
            {
                etaGeometry = ParametricSpectra.SampleIndex(dielectricEta, null, texEval, ctx, ref lambda);
            }
            etaGeometry = std.max<Float>(etaGeometry, 1.0001f);
            // (rough enough for the microfacet formulas: this BxDF has no specular case)
            Float anx = std.max<Float>(q.anx, 2e-3f), any = std.max<Float>(q.any, 2e-3f);
            return new HazyDielectricBxDF(etaGeometry, std.max<Float>(q.dielectricEta, 1.0001f), MicrofacetDistribution.Create(distribution, anx, any, gamma),
                                          MicrofacetDistribution.Create(distribution, std.max(q.awx, anx), std.max(q.awy, any), gamma), q.beta);
        }

        // The optical thickness of the layer under the interface (0: none)
        public Float TransmissionLayer<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx)
            where TextureEvaluator : ITextureEvaluator
        {
            return transmissionHaziness != null ? HazyTransmissionBxDF.OpticalThickness(texEval.Apply(transmissionHaziness, ctx)) : 0;
        }

        public override BSDF GetBSDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx, ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            if (kind == HazyGlossKind.Dielectric && SeparateTransmission())
            {
                HazyGlossParameters q = Parameters(texEval, ctx, ref lambda);
                HazyDielectricBxDF surface = InterfaceBxDF(texEval, ctx, ref lambda, q);
                Float tau = TransmissionLayer(texEval, ctx);
                if (tau > 0)
                {
                    return new BSDF(ctx.n, ctx.ns, ctx.dpdus, buf.Alloc(new HazyTransmissionBxDF(surface, tau, transmissionHazeG, layerDepth, layerSamples)));
                }
                return new BSDF(ctx.n, ctx.ns, ctx.dpdus, buf.Alloc(surface));
            }
            HazyGlossBxDF bxdf = GetBxDF(texEval, ctx, ref lambda);
            return new BSDF(ctx.n, ctx.ns, ctx.dpdus, buf.Alloc(bxdf));
        }

        public static HazyGlossMaterial Create(TextureParameterDictionary parameters, Image normalMap, FileLoc loc, Allocator alloc)
        {
            HazyGlossMaterial m = new HazyGlossMaterial();
            string modeName = parameters.GetOneString("mode", "perceptual");
            if (modeName == "perceptual")
            {
                m.mode = HazyGlossMode.Perceptual;
            }
            else if (modeName == "physical")
            {
                m.mode = HazyGlossMode.Physical;
            }
            else
            {
                ErrorExit(loc, "\"mode\" must be \"perceptual\" or \"physical\"");
            }
            string kindName = parameters.GetOneString("kind", "conductor");
            if (kindName == "conductor")
            {
                m.kind = HazyGlossKind.Conductor;
            }
            else if (kindName == "dielectric")
            {
                m.kind = HazyGlossKind.Dielectric;
            }
            else if (kindName == "plastic")
            {
                m.kind = HazyGlossKind.Plastic;
            }
            else
            {
                ErrorExit(loc, "\"kind\" must be \"conductor\", \"dielectric\" or \"plastic\"");
            }

            m.uRoughness = parameters.GetFloatTextureOrNull("uroughness", alloc);
            m.vRoughness = parameters.GetFloatTextureOrNull("vroughness", alloc);
            if (m.uRoughness == null)
            {
                m.uRoughness = parameters.GetFloatTexture("roughness", 0.02f, alloc);
            }
            if (m.vRoughness == null)
            {
                m.vRoughness = parameters.GetFloatTexture("roughness", 0.02f, alloc);
            }

            if (m.mode == HazyGlossMode.Perceptual)
            {
                m.reflectance = parameters.GetSpectrumTexture("reflectance", null, SpectrumType.Albedo, alloc);
                if (m.reflectance == null)
                {
                    m.reflectance = new SpectrumConstantTexture(new ConstantSpectrum(0.5f));
                }
                m.edgeTint = parameters.GetSpectrumTexture("edgetint", null, SpectrumType.Albedo, alloc);
                if (m.edgeTint == null)
                {
                    m.edgeTint = new SpectrumConstantTexture(new ConstantSpectrum(1f));
                }
                m.haziness = parameters.GetFloatTexture("haziness", 0f, alloc);
                FloatTexture extent = parameters.GetFloatTexture("hazeextent", 5f, alloc);
                m.hazeU = parameters.GetFloatTextureOrNull("hazeextent.u", alloc);
                m.hazeV = parameters.GetFloatTextureOrNull("hazeextent.v", alloc);
                if (m.hazeU == null)
                {
                    m.hazeU = extent;
                }
                if (m.hazeV == null)
                {
                    m.hazeV = extent;
                }
            }
            else
            {
                m.beta = parameters.GetFloatTexture("beta", 0f, alloc);
                m.wideU = parameters.GetFloatTextureOrNull("wideuroughness", alloc);
                m.wideV = parameters.GetFloatTextureOrNull("widevroughness", alloc);
                if (m.wideU == null)
                {
                    m.wideU = parameters.GetFloatTexture("wideroughness", 0.2f, alloc);
                }
                if (m.wideV == null)
                {
                    m.wideV = parameters.GetFloatTexture("wideroughness", 0.2f, alloc);
                }
                if (m.kind == HazyGlossKind.Conductor)
                {
                    m.eta = parameters.GetSpectrumTextureOrNull("eta", SpectrumType.Unbounded, alloc);
                    m.k = parameters.GetSpectrumTextureOrNull("k", SpectrumType.Unbounded, alloc);
                    if (m.eta == null)
                    {
                        m.eta = new SpectrumConstantTexture(GetNamedSpectrum("metal-Cu-eta"));
                    }
                    if (m.k == null)
                    {
                        m.k = new SpectrumConstantTexture(GetNamedSpectrum("metal-Cu-k"));
                    }
                }
                else
                {
                    m.dielectricEta = ParametricSpectra.Index(parameters, "ior", alloc);
                    if (m.dielectricEta == null)
                    {
                        m.dielectricEta = new ConstantSpectrum(1.5f);
                    }
                }
            }
            if (m.kind == HazyGlossKind.Plastic)
            {
                m.baseReflectance = parameters.GetSpectrumTextureOrNull("base.reflectance", SpectrumType.Albedo, alloc);
            }
            if (m.kind == HazyGlossKind.Dielectric)
            {
                m.transmissionHaziness = parameters.GetFloatTextureOrNull("transmissionhaziness", alloc);
                m.transmissionHazeG = Clamp(parameters.GetOneFloat("transmissionhazeg", 0.9f), -0.99f, 0.99f);
                m.layerDepth = parameters.GetOneInt("maxdepth", 100);
                m.layerSamples = parameters.GetOneInt("nsamples", 1);
                if (m.mode == HazyGlossMode.Perceptual)
                {
                    // "ior" in the perceptual mode: the index of the refraction, which the haze then leaves alone
                    m.dielectricEta = ParametricSpectra.Index(parameters, "ior", alloc);
                }
            }
            m.displacement = parameters.GetFloatTextureOrNull("displacement", alloc);
            string masking = parameters.GetOneString("masking", "separate");
            if (masking != "separate" && masking != "intertwined")
            {
                ErrorExit(loc, "\"masking\" must be \"separate\" or \"intertwined\"");
            }
            m.intertwined = masking == "intertwined";
            m.scatterDepth = parameters.GetOneInt("scatterdepth", 1);
            if (m.intertwined && m.scatterDepth > 1)
            {
                ErrorExit(loc, "intertwined masking supports single scattering only (scatterdepth 1)");
            }
            if (m.kind == HazyGlossKind.Dielectric && m.SeparateTransmission() && (m.intertwined || m.scatterDepth > 1))
            {
                ErrorExit(loc, "\"transmissionhaziness\" and a fixed \"ior\" need the separate masking and single scattering");
            }
            if (m.kind == HazyGlossKind.Plastic && m.scatterDepth > 1)
            {
                ErrorExit(loc, "the plastic kind supports single scattering only (scatterdepth 1)");
            }
            m.bezierW = parameters.GetOneFloat("smoothing", 0f);
            m.normalMap = normalMap;
            m.remapRoughness = parameters.GetOneBool("remaproughness", false);
            m.coupledBase = parameters.GetOneBool("base.coupled", false);
            m.distribution = MicrofacetDistribution.ParseType(parameters.GetOneString("distribution", "ggx"), loc);
            if (m.distribution == MicrofacetDistributionType.Charlie)
            {
                ErrorExit(loc, "hazygloss: the \"charlie\" distribution has no peak at the normal; use \"ggx\", \"beckmann\" or \"studentt\"");
            }
            m.gamma = parameters.GetOneFloat("gamma", 2f);
            return m;
        }
    }
}
