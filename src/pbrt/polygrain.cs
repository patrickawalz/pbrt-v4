// (not in pbrt-v4) polygrain.cs
//
// PolyGrainBxDF and PolyGrainMaterial: a direct port of the C++ fork (bxdfs.h, materials.h/.cpp), on util/micrograin.cs, with
// the multiple scattering of polygrain_walk.cs ("scatterdepth" above 1), which is not of the fork.

using System;
using Attenuation = pbrt.PolarisedLightSystem.Attenuation;

namespace pbrt
{
    public enum PolyGrainSpecular
    {
        None,
        Conductor,
        Dielectric
    }

    // PolyGrainBxDF Definition
    //
    // Discrete polydisperse anisotropic micrograin BSDF (K. Xu, S. Lucas, M. Ribardiere, B. Bringier, P. Barla, "A Discrete
    // Polydisperse Anisotropic BSDF Model based on the Micrograin Framework", CGF 2026 [XLRBB26]) built on the fully-correlated
    // micrograin model of Lucas et al. (ACM TOG 2024 [LRPB24]). The geometry/statistics live in util/micrograin.cs; this class
    // adds the reflectances (per grain type: diffuse albedo, conductor or dielectric Fresnel) and the bulk BSDF, and implements
    // f / Sample_f / PDF:
    //
    //   f(i,o) = tau0bar * (f_s + f_d) + Vp(i,o) f_B(i,o)                          [XLRBB26 Eq. 6]
    //   f_s = D(h) Gloc/(4|i.n||o.n|) sum_j p(j|h) Gdist~_j(i,o,h) F_j(i.h)          [Eq. 16]
    //   f_d = int |i.m||o.m|/(|i.n||o.n|) D(m) Gloc sum_j p(j|m) Gdist~_j K_j/pi T_j dm [Eq. 17]
    //   Vp  = (1-tau0bar) prod_j Gdist_j(i,o,0)                                     [Eq. 15]
    //
    // Differences from the paper (all choices where it leaves freedom):
    //  * f_d has no closed form (LRPB24 section 4.3 evaluates it stochastically); here it is a K-sample unbiased Monte Carlo
    //    estimate with samples drawn from the exact joint density p(j,m) (so the estimator is exact in expectation); the random
    //    numbers are a hash of (wo,wi) so f is a deterministic function and Sample_f/PDF/f are mutually consistent.
    //  * Sampling is the procedure of XLRBB26 section 6 / Appendix E (bulk with probability pB, slice, grain type,
    //    diffuse/specular, normal), implemented in closed form. The extra random decisions use a hash-seeded RNG because pbrt's
    //    interface provides only (uc, u).
    //  * Grains sit on the front (z>0) face only: for directions below the surface only the bulk BSDF is evaluated, and for one
    //    direction above/one below the pore visibility is unidirectional.
    // (The bulk is any BxDF: the three kinds of the fork are its DiffuseBxDF, ConductorBxDF and DielectricBxDF.)
    public sealed unsafe partial class PolyGrainBxDF : BxDF
    {
        public const int kMaxTypes = mg.PolyGrain.kMaxTypes;

        public PolyGrainBxDF() { }

        public PolyGrainBxDF(mg.PolyGrain dist, int nSamples)
        {
            this.dist = dist;
            this.nSamples = nSamples;
        }

        public static string Name() { return "PolyGrainBxDF"; }

        public override string ToString()
        {
            return StringPrintf("[ PolyGrainBxDF grain types: %d tau0bar: %f nSamples: %d scatter depth: %d ]", dist.n, (Float)dist.tauBar0, nSamples, maxOrder);
        }

        // Grain reflectances (call once per type before use)
        public void SetGrainDiffuse(int j, SampledSpectrum Kd)
        {
            this.Kd[j] = Kd;
            hasDiffuse = hasDiffuse || (Kd.MaxComponentValue() > 0);
        }

        public void SetGrainConductor(int j, SampledSpectrum eta, SampledSpectrum k)
        {
            spec[j] = PolyGrainSpecular.Conductor;
            this.eta[j] = eta;
            this.k[j] = k;
        }

        public void SetGrainDielectric(int j, Float eta)
        {
            spec[j] = PolyGrainSpecular.Dielectric;
            this.eta[j] = new SampledSpectrum(eta);
        }

        // Bulk BSDF
        public void SetBulk(BxDF b)
        {
            bulk = b;
        }

        public override BxDFFlags Flags()
        {
            BxDFFlags fl = bulk.Flags() | BxDFFlags.Reflection;
            if (hasDiffuse)
            {
                fl = fl | BxDFFlags.Diffuse;
            }
            for (int j = 0; j < dist.n; ++j)
            {
                if (spec[j] != PolyGrainSpecular.None)
                {
                    fl = fl | BxDFFlags.Glossy;
                }
            }
            return fl;
        }

        public override void Regularize()
        {
            bulk.Regularize();
        }

        public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            if (maxOrder > 1)
            {
                return WalkF(wo, wi, mode);
            }
            if (wo.z <= 0 && wi.z <= 0)
            {
                return bulk.f(wo, wi, mode);
            }
            SampledSpectrum fb = bulk.f(wo, wi, mode);
            SampledSpectrum res = new SampledSpectrum(0f);
            if ((bool)fb)
            {
                res = BulkVis(wo, wi) * fb;
            }
            if (wo.z > 0 && wi.z > 0)
            {
                res += (Float)dist.tauBar0 * GrainF(wo, wi);
            }
            return res;
        }

        public override pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if (maxOrder > 1)
            {
                return WalkSample(wo, uc, u, mode, sampleFlags);
            }
            if (wo.z == 0)
            {
                return default;
            }
            bool up = wo.z > 0;
            bool allowR = (sampleFlags & BxDFReflTransFlags.Reflection) != 0;
            Float pB = (up && allowR) ? BulkProbability(wo) : 1;
            if (uc < pB)
            {
                // Bulk component
                Float ub = pB > 0 ? std.min<Float>(uc / pB, OneMinusEpsilon) : 0;
                pstd.optional<BSDFSample> sampled = bulk.Sample_f(wo, ub, u, mode, sampleFlags);
                // (not as the fork, which also drops a direction where the bulk is black: the grains reflect there, and the density
                // below counts this way of sampling it. A black bulk under grains lost the share pB of their light.)
                if (!sampled || sampled.value().pdf == 0)
                {
                    return default;
                }
                BSDFSample bs = sampled.value();
                if (bs.IsSpecular() && !(bool)bs.f)
                {
                    return default;
                }
                if (bs.IsSpecular())
                {
                    if (up && bs.wi.z != 0)
                    {
                        bs.f *= BulkVis(wo, bs.wi);
                    }
                    bs.pdf *= pB;
                    return bs;
                }
                Float pdfBulk = PDF(wo, bs.wi, mode, sampleFlags);
                SampledSpectrum fBulk = f(wo, bs.wi, mode);
                if (pdfBulk == 0 || !(bool)fBulk)
                {
                    return default;
                }
                return new BSDFSample(fBulk, bs.wi, pdfBulk, bs.flags, bs.eta, false);
            }
            // Grain component
            RNG rng = new RNG(Hash(wo, uc), Hash(u));
            Float r0 = rng.Uniform<Float>(), r1 = rng.Uniform<Float>(), r2 = rng.Uniform<Float>();
            int k, type;
            double pj;
            dist.SampleSliceType(r0, r1, out k, out type, out pj);
            Vector3f wi;
            BxDFFlags flags;
            if (r2 < DiffuseProbability(type, wo))
            {
                wi = SampleCosineHemisphere(u);
                flags = BxDFFlags.DiffuseReflection;
            }
            else
            {
                mg.Vec3d md;
                double pdfm;
                if (!dist.SampleNormal(k, type, u[0], u[1], out md, out pdfm))
                {
                    return default;
                }
                Vector3f m = new Vector3f((Float)md.x, (Float)md.y, (Float)md.z);
                if (Dot(wo, m) <= 0)
                {
                    return default;
                }
                wi = -wo + 2 * Dot(wo, m) * m;
                flags = BxDFFlags.GlossyReflection;
            }
            if (wi.z <= 0)
            {
                return default;
            }
            Float pdf = PDF(wo, wi, mode, sampleFlags);
            SampledSpectrum fv = f(wo, wi, mode);
            if (pdf == 0 || !(bool)fv)
            {
                return default;
            }
            return new BSDFSample(fv, wi, pdf, flags, 1f, false);
        }

        public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if (maxOrder > 1)
            {
                return WalkPDF(wo, wi, mode, sampleFlags);
            }
            if (wo.z == 0)
            {
                return 0;
            }
            bool up = wo.z > 0;
            bool allowR = (sampleFlags & BxDFReflTransFlags.Reflection) != 0;
            Float pB = (up && allowR) ? BulkProbability(wo) : 1;
            Float pdf = pB * bulk.PDF(wo, wi, mode, sampleFlags);
            if (up && allowR && wi.z > 0 && pB < 1)
            {
                pdf += (1 - pB) * GrainPDF(wo, wi);
            }
            return pdf;
        }

        private static mg.Vec3d V3(Vector3f v) { return new mg.Vec3d(v.x, v.y, v.z); }

        // ---- pore visibility and bulk selection probability (Eq. 15, 18)
        private Float BulkVis(Vector3f wo, Vector3f wi)
        {
            Float v = 1 - (Float)dist.tauBar0;
            if (wo.z > 0 && wi.z > 0)
            {
                return v * (Float)dist.BulkVisBi(V3(wo), V3(wi));
            }
            if (wo.z > 0)
            {
                return v * (Float)dist.BulkVisUni(V3(wo));
            }
            if (wi.z > 0)
            {
                return v * (Float)dist.BulkVisUni(V3(wi));
            }
            return 1;
        }

        private Float BulkProbability(Vector3f wo)
        {
            double g = (1 - dist.tauBar0) * dist.BulkVisUni(V3(wo));
            return (Float)(g / (dist.tauBar0 + g));
        }

        // ---- grain Fresnel and diffuse/specular selection (Eq. 27 / 38)
        private SampledSpectrum GrainFresnel(int j, Float cosTheta)
        {
            switch (spec[j])
            {
            case PolyGrainSpecular.Conductor:
                return FrComplex(cosTheta, eta[j], k[j]);
            case PolyGrainSpecular.Dielectric:
                return new SampledSpectrum(FrDielectric(cosTheta, eta[j][0]));
            default:
                return new SampledSpectrum(0f);
            }
        }

        private Float DiffuseProbability(int j, Vector3f wo)
        {
            Float kd = Kd[j].Average();
            Float fr = GrainFresnel(j, wo.z).Average();
            return (kd + fr > 0) ? kd / (kd + fr) : 0;
        }

        // ---- grain BRDF  f_s + f_d   (without the tau0bar weight)
        private SampledSpectrum GrainF(Vector3f wo, Vector3f wi)
        {
            SampledSpectrum res = GrainSpecularF(wo, wi);
            // diffuse term (Eq. 17), unbiased K-sample estimate from p(j,m)
            if (hasDiffuse)
            {
                mg.Vec3d od = V3(wo), id = V3(wi);
                RNG rng = new RNG(Hash(wo, nSamples), Hash(wi));
                SampledSpectrum acc = new SampledSpectrum(0f);
                for (int s = 0; s < nSamples; ++s)
                {
                    int kk, type;
                    double pjoint;
                    Float u0 = rng.Uniform<Float>(), u1 = rng.Uniform<Float>();
                    dist.SampleSliceType(u0, u1, out kk, out type, out pjoint);
                    mg.Vec3d md;
                    double pm;
                    Float uh = rng.Uniform<Float>(), up = rng.Uniform<Float>();
                    if (!dist.SampleNormal(kk, type, uh, up, out md, out pm) || !(bool)Kd[type])
                    {
                        continue;
                    }
                    Vector3f m = new Vector3f((Float)md.x, (Float)md.y, (Float)md.z);
                    Float wom = Dot(wo, m), wim = Dot(wi, m);
                    if (wom <= 0 || wim <= 0)
                    {
                        continue;  // local masking
                    }
                    double g = dist.GTilde(type, od, id, md);
                    SampledSpectrum T = (new SampledSpectrum(1f) - GrainFresnel(type, wom)) * (new SampledSpectrum(1f) - GrainFresnel(type, wim));
                    acc += (Float)(g * wom * wim / (AbsCosTheta(wo) * AbsCosTheta(wi) * m.z * Pi)) * Kd[type] * T;
                }
                res += acc / (Float)nSamples;
            }
            return res;
        }

        // specular term (Eq. 16):  sum_j p(j,h) Gdist~_j F_j / (4 h.z |wo.z||wi.z|)
        private SampledSpectrum GrainSpecularF(Vector3f wo, Vector3f wi)
        {
            Vector3f h = wo + wi;
            Float hl = Length(h);
            if (hl == 0)
            {
                return new SampledSpectrum(0f);
            }
            h /= hl;
            mg.Vec3d hd = V3(h);
            mg.Vec3d od = V3(wo), id = V3(wi);
            SampledSpectrum fs = new SampledSpectrum(0f);
            for (int j = 0; j < dist.n; ++j)
            {
                if (spec[j] == PolyGrainSpecular.None)
                {
                    continue;
                }
                double pj = dist.PTypeNormal(j, hd);
                if (pj <= 0)
                {
                    continue;
                }
                double g = dist.GTilde(j, od, id, hd);
                fs += (Float)(pj * g) * GrainFresnel(j, AbsDot(wo, h));
            }
            fs /= (4 * h.z * AbsCosTheta(wo) * AbsCosTheta(wi));
            return fs;
        }

        // ---- density of the grain sampling procedure at wi (given wo)
        private Float GrainPDF(Vector3f wo, Vector3f wi)
        {
            Vector3f h = wo + wi;
            Float hl = Length(h);
            if (hl == 0 || wi.z <= 0)
            {
                return 0;
            }
            h /= hl;
            mg.Vec3d hd = V3(h);
            double pd = 0, ps = 0;
            for (int j = 0; j < dist.n; ++j)
            {
                double pdi = DiffuseProbability(j, wo);
                pd += dist.typeProb[j] * pdi;
                ps += (1 - pdi) * dist.PTypeNormal(j, hd);
            }
            Float c = Dot(wo, h);
            double pdf = pd * wi.z / Pi;
            if (c > 0)
            {
                pdf += ps / (4 * c);
            }
            return (Float)pdf;
        }

        private mg.PolyGrain dist = new mg.PolyGrain();
        private int nSamples = 4;
        private SampledSpectrum[] Kd = new SampledSpectrum[kMaxTypes], eta = new SampledSpectrum[kMaxTypes], k = new SampledSpectrum[kMaxTypes];
        private PolyGrainSpecular[] spec = new PolyGrainSpecular[kMaxTypes];
        private bool hasDiffuse = false;
        private BxDF bulk = new DiffuseBxDF(new SampledSpectrum(0.5f));
        // (ours) The scatterings on the grains and the bulk that are followed: 1 is the model of the paper
        public int maxOrder = 1;
    }

    // PolyGrainMaterial Definition
    // Discrete polydisperse micrograin material (Xu et al. 2026): up to eight grain types ("grain<N>.*") deposited on a bulk BSDF.
    //
    // Parameters (per grain type N = 0..7, a type exists when "grainN.tau" is given):
    //   grainN.tau          filling factor tau_{N,0} in (0,1)  (float texture)
    //   grainN.radius       radius (only ratios matter), default 1
    //   grainN.betax/betay  lateral stretching 1/beta (anisotropy), default 1 (or "grainN.beta")
    //   grainN.rotation     rotation of the anisotropy axes in the tangent plane, degrees
    //   grainN.reflectance  diffuse albedo K_d (spectrum texture), default 0
    //   grainN.eta/.k       conductor index (spectrum) ; grainN.ior  dielectric (plastic) index
    //   (a type with neither eta/k nor ior has no specular component)
    // Bulk: "string bulk" = diffuse | conductor | dielectric with bulk.reflectance, bulk.eta/bulk.k, bulk.ior and bulk.roughness.
    //   integer nsamples    the samples of the diffuse term of f (4)
    //   integer scatterdepth  (ours) the scatterings that are followed (1: the model of the paper; polygrain_walk.cs)
    public sealed class PolyGrainTypeParams
    {
        public bool enabled = false;
        public FloatTexture tau;                                       // surface filling factor tau_{j,0}
        public Float radius = 1, betax = 1, betay = 1, rotation = 0;  // rotation in degrees
        public SpectrumTexture reflectance;                            // diffuse albedo K_d (may be null)
        public SpectrumTexture eta, k;                                 // conductor (may be null)
        public Spectrum dielectricEta;                                 // dielectric index (may be null)
    }

    public enum PolyGrainBulk
    {
        Diffuse,
        Conductor,
        Dielectric
    }

    public sealed unsafe class PolyGrainMaterial : Material
    {
        public PolyGrainMaterial(PolyGrainTypeParams[] grains, PolyGrainBulk bulk, SpectrumTexture bulkReflectance, SpectrumTexture bulkEta, SpectrumTexture bulkK,
                                 Spectrum bulkDielectricEta, FloatTexture bulkRoughness, FloatTexture displacement, Image normalMap, bool remapRoughness,
                                 int nSamples)
        {
            this.bulk = bulk;
            this.bulkReflectance = bulkReflectance;
            this.bulkEta = bulkEta;
            this.bulkK = bulkK;
            this.bulkDielectricEta = bulkDielectricEta;
            this.bulkRoughness = bulkRoughness;
            this.displacement = displacement;
            this.normalMap = normalMap;
            this.remapRoughness = remapRoughness;
            this.nSamples = nSamples;
            for (int i = 0; i < PolyGrainBxDF.kMaxTypes; ++i)
            {
                this.grains[i] = grains[i];
            }
        }

        public static string Name() { return "PolyGrainMaterial"; }

        public override bool CanEvaluateTextures<TextureEvaluator>(TextureEvaluator texEval)
        {
            if (!texEval.CanEvaluate(new FloatTexture[] { bulkRoughness }, new SpectrumTexture[] { bulkReflectance, bulkEta, bulkK }))
            {
                return false;
            }
            for (int i = 0; i < PolyGrainBxDF.kMaxTypes; ++i)
            {
                if (grains[i].enabled &&
                    !texEval.CanEvaluate(new FloatTexture[] { grains[i].tau }, new SpectrumTexture[] { grains[i].reflectance, grains[i].eta, grains[i].k }))
                {
                    return false;
                }
            }
            return true;
        }

        public override FloatTexture GetDisplacement() { return displacement; }
        public override Image GetNormalMap() { return normalMap; }
        public override bool HasSubsurfaceScattering() { return false; }

        public override BSSRDF GetBSSRDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx, ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            return null;
        }

        public override string ToString()
        {
            int n = 0;
            foreach (PolyGrainTypeParams g in grains)
            {
                n += g.enabled ? 1 : 0;
            }
            return StringPrintf("[ PolyGrainMaterial grain types: %d ]", n);
        }

        public PolyGrainBxDF GetBxDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx, ref SampledWavelengths lambda)
            where TextureEvaluator : ITextureEvaluator
        {
            mg.PolyGrain dist = new mg.PolyGrain();
            dist.n = 0;
            SampledSpectrum[] Kd = new SampledSpectrum[PolyGrainBxDF.kMaxTypes], ge = new SampledSpectrum[PolyGrainBxDF.kMaxTypes],
                              gk = new SampledSpectrum[PolyGrainBxDF.kMaxTypes];
            Float[] gdiel = new Float[PolyGrainBxDF.kMaxTypes];
            int[] kind = new int[PolyGrainBxDF.kMaxTypes];  // 0 none, 1 conductor, 2 dielectric
            for (int i = 0; i < PolyGrainBxDF.kMaxTypes; ++i)
            {
                PolyGrainTypeParams g = grains[i];
                if (!g.enabled)
                {
                    continue;
                }
                Float tau = Clamp(texEval.Apply(g.tau, ctx), 0, 0.9999f);
                if (tau <= 0)
                {
                    continue;
                }
                int j = dist.n++;
                dist.SetType(j, tau, g.radius, g.betax, g.betay, Radians(g.rotation));
                Kd[j] = g.reflectance != null ? Clamp(texEval.Apply(g.reflectance, ctx, lambda), 0, 1) : new SampledSpectrum(0f);
                kind[j] = 0;
                if (g.eta != null && g.k != null)
                {
                    kind[j] = 1;
                    ge[j] = texEval.Apply(g.eta, ctx, lambda);
                    gk[j] = texEval.Apply(g.k, ctx, lambda);
                }
                else if (g.dielectricEta != null)
                {
                    kind[j] = 2;
                    gdiel[j] = g.dielectricEta.Evaluate(lambda[0]);
                    if (!(g.dielectricEta is ConstantSpectrum))
                    {
                        lambda.TerminateSecondary();
                    }
                }
            }
            dist.Finalize_();
            PolyGrainBxDF bxdf = new PolyGrainBxDF(dist, nSamples);
            bxdf.maxOrder = scatterDepth;
            bxdf.reciprocal = reciprocal;
            for (int j = 0; j < dist.n; ++j)
            {
                bxdf.SetGrainDiffuse(j, Kd[j]);
                if (kind[j] == 1)
                {
                    bxdf.SetGrainConductor(j, ge[j], gk[j]);
                }
                else if (kind[j] == 2)
                {
                    bxdf.SetGrainDielectric(j, gdiel[j]);
                }
            }
            Float rough = texEval.Apply(bulkRoughness, ctx);
            if (remapRoughness)
            {
                rough = TrowbridgeReitzDistribution.RoughnessToAlpha(rough);
            }
            switch (bulk)
            {
            case PolyGrainBulk.Diffuse:
                bxdf.SetBulk(new DiffuseBxDF(Clamp(texEval.Apply(bulkReflectance, ctx, lambda), 0, 1)));
                break;
            case PolyGrainBulk.Conductor:
                bxdf.SetBulk(new ConductorBxDF(new TrowbridgeReitzDistribution(rough, rough), texEval.Apply(bulkEta, ctx, lambda), texEval.Apply(bulkK, ctx, lambda)));
                break;
            case PolyGrainBulk.Dielectric:
            {
                Float e = bulkDielectricEta.Evaluate(lambda[0]);
                if (!(bulkDielectricEta is ConstantSpectrum))
                {
                    lambda.TerminateSecondary();
                }
                bxdf.SetBulk(new DielectricBxDF(e, new TrowbridgeReitzDistribution(rough, rough)));
                break;
            }
            }
            return bxdf;
        }

        public override BSDF GetBSDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx, ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            PolyGrainBxDF bxdf = GetBxDF(texEval, ctx, ref lambda);
            return new BSDF(ctx.n, ctx.ns, ctx.dpdus, buf.Alloc(bxdf));
        }

        public static PolyGrainMaterial Create(TextureParameterDictionary parameters, Image normalMap, FileLoc loc, Allocator alloc)
        {
            PolyGrainTypeParams[] grains = new PolyGrainTypeParams[PolyGrainBxDF.kMaxTypes];
            for (int i = 0; i < PolyGrainBxDF.kMaxTypes; ++i)
            {
                grains[i] = new PolyGrainTypeParams();
                string p = "grain" + i.ToString() + ".";
                FloatTexture tau = parameters.GetFloatTextureOrNull(p + "tau", alloc);
                if (tau == null)
                {
                    continue;
                }
                PolyGrainTypeParams g = grains[i];
                g.enabled = true;
                g.tau = tau;
                g.radius = parameters.GetOneFloat(p + "radius", 1f);
                Float beta = parameters.GetOneFloat(p + "beta", 1f);
                g.betax = parameters.GetOneFloat(p + "betax", beta);
                g.betay = parameters.GetOneFloat(p + "betay", beta);
                if (g.radius <= 0 || g.betax <= 0 || g.betay <= 0)
                {
                    ErrorExit(loc, StringPrintf("%sradius, %sbetax and %sbetay must be positive", p, p, p));
                }
                g.rotation = parameters.GetOneFloat(p + "rotation", 0f);
                g.reflectance = parameters.GetSpectrumTextureOrNull(p + "reflectance", SpectrumType.Albedo, alloc);
                g.eta = parameters.GetSpectrumTextureOrNull(p + "eta", SpectrumType.Unbounded, alloc);
                g.k = parameters.GetSpectrumTextureOrNull(p + "k", SpectrumType.Unbounded, alloc);
                if ((g.eta == null) != (g.k == null))
                {
                    ErrorExit(loc, StringPrintf("\"%seta\" and \"%sk\" must be given together", p, p));
                }
                g.dielectricEta = ParametricSpectra.Index(parameters, p + "ior", alloc);
                if (g.eta != null && g.dielectricEta != null)
                {
                    ErrorExit(loc, StringPrintf("grain type %d: conductor (eta,k) and dielectric (ior) given", i));
                }
            }
            string b = parameters.GetOneString("bulk", "diffuse");
            PolyGrainBulk bulk = PolyGrainBulk.Diffuse;
            SpectrumTexture bulkReflectance = null, bulkEta = null, bulkK = null;
            Spectrum bulkIor = null;
            if (b == "diffuse")
            {
                bulk = PolyGrainBulk.Diffuse;
                bulkReflectance = parameters.GetSpectrumTexture("bulk.reflectance", null, SpectrumType.Albedo, alloc);
                if (bulkReflectance == null)
                {
                    bulkReflectance = new SpectrumConstantTexture(new ConstantSpectrum(0.5f));
                }
            }
            else if (b == "conductor")
            {
                bulk = PolyGrainBulk.Conductor;
                bulkEta = parameters.GetSpectrumTextureOrNull("bulk.eta", SpectrumType.Unbounded, alloc);
                bulkK = parameters.GetSpectrumTextureOrNull("bulk.k", SpectrumType.Unbounded, alloc);
                if (bulkEta == null)
                {
                    bulkEta = new SpectrumConstantTexture(GetNamedSpectrum("metal-Cu-eta"));
                }
                if (bulkK == null)
                {
                    bulkK = new SpectrumConstantTexture(GetNamedSpectrum("metal-Cu-k"));
                }
            }
            else if (b == "dielectric")
            {
                bulk = PolyGrainBulk.Dielectric;
                bulkIor = ParametricSpectra.Index(parameters, "bulk.ior", alloc);
                if (bulkIor == null)
                {
                    bulkIor = new ConstantSpectrum(1.5f);
                }
            }
            else
            {
                ErrorExit(loc, "\"bulk\" must be \"diffuse\", \"conductor\" or \"dielectric\"");
            }
            FloatTexture bulkRoughness = parameters.GetFloatTexture("bulk.roughness", 0f, alloc);
            FloatTexture displacement = parameters.GetFloatTextureOrNull("displacement", alloc);
            PolyGrainMaterial material = new PolyGrainMaterial(grains, bulk, bulkReflectance, bulkEta, bulkK, bulkIor, bulkRoughness, displacement, normalMap,
                                                               parameters.GetOneBool("remaproughness", true), parameters.GetOneInt("nsamples", 4));
            material.scatterDepth = parameters.GetOneInt("scatterdepth", 1);
            material.reciprocal = parameters.GetOneBool("reciprocal", true);
            return material;
        }

        // (ours) "integer scatterdepth": the scatterings on the grains and the bulk that are followed (1: the paper)
        public int scatterDepth = 1;
        // (ours) "bool reciprocal": the later scatterings walked from both directions (polygrain_walk.cs)
        public bool reciprocal = true;

        private PolyGrainTypeParams[] grains = new PolyGrainTypeParams[PolyGrainBxDF.kMaxTypes];
        private PolyGrainBulk bulk;
        private SpectrumTexture bulkReflectance, bulkEta, bulkK;
        private Spectrum bulkDielectricEta;
        private FloatTexture bulkRoughness, displacement;
        private Image normalMap;
        private bool remapRoughness;
        private int nSamples;
    }
}
