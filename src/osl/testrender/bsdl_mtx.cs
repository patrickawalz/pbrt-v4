// OSL port: the MaterialX lobes of libbsdl (BSDL/MTX/*) as testrender uses them (testrender/shading.cpp, BSDL_WRAP)
//
// conductor_bsdf, dielectric_bsdf, generalized_schlick_bsdf, sheen_bsdf, burley_diffuse_bsdf, oren_nayar_diffuse_bsdf and translucent_bsdf. Every lobe is
// a BSDF of the test renderer: the world space directions are turned into the local frame of the lobe, the lobe is evaluated or sampled there, and the RGB
// weight is handed back. The Data structs are the closure parameter blocks of libbsdl (their field order is the layout the closures are registered with).

using System;
using System.Runtime.InteropServices;
using OIIO;

namespace OSL
{
    /// The closure ids of the MaterialX closures and of libbsdl (testrender/shading.h)
    public static class MxClosureIDs
    {
        public const int DEBUG_ID = 13;
        public const int HOLDOUT_ID = 14;
        public const int MX_OREN_NAYAR_DIFFUSE_ID = 15;
        public const int MX_BURLEY_DIFFUSE_ID = 16;
        public const int MX_DIELECTRIC_ID = 17;
        public const int MX_CONDUCTOR_ID = 18;
        public const int MX_GENERALIZED_SCHLICK_ID = 19;
        public const int MX_TRANSLUCENT_ID = 20;
        public const int MX_TRANSPARENT_ID = 21;
        public const int MX_SUBSURFACE_ID = 22;
        public const int MX_SHEEN_ID = 23;
        public const int MX_UNIFORM_EDF_ID = 24;
        public const int MX_ANISOTROPIC_VDF_ID = 25;
        public const int MX_MEDIUM_VDF_ID = 26;
        public const int MX_LAYER_ID = 27;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct MxSubsurfaceParams
    {
        public Vec3 N;
        public Color3 albedo;
        public Color3 radius;
        public float anisotropy;
        public ulong label;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MxUniformEdfParams
    {
        public Color3 emittance;
        public ulong label;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct MxLayerParams
    {
        public ClosureColor* top;
        public ClosureColor* baseclosure;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MxAnisotropicVdfParams
    {
        public Color3 albedo;
        public Color3 extinction;
        public float anisotropy;
        public ulong label;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MxMediumVdfParams
    {
        public Color3 albedo;
        public float transmission_depth;
        public Color3 transmission_color;
        public float anisotropy;
        public float ior;
        public int priority;
        public ulong label;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct ConductorData
    {
        public Vec3 N, U;
        public float roughness_x;
        public float roughness_y;
        public Color3 IOR;
        public Color3 extinction;
        public ulong distribution;
        public float thinfilm_thickness;
        public float thinfilm_ior;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct DielectricData
    {
        public void* closure;
        public Vec3 N;
        public Vec3 U;
        public Color3 refl_tint;
        public Color3 refr_tint;
        public float roughness_x;
        public float roughness_y;
        public float IOR;
        public ulong distribution;
        public float thinfilm_thickness;
        public float thinfilm_ior;
        public float dispersion;
        public Color3 absorption;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct SchlickData
    {
        public void* closure;
        public Vec3 N;
        public Vec3 U;
        public Color3 refl_tint;
        public Color3 refr_tint;
        public float roughness_x;
        public float roughness_y;
        public Color3 F0;
        public Color3 F90;
        public float exponent;
        public ulong distribution;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct SheenData
    {
        public void* closure;
        public Vec3 N;
        public Color3 albedo;
        public float roughness;
        public int mode;
        public ulong label;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BurleyDiffuseData
    {
        public Vec3 N;
        public Color3 albedo;
        public float roughness;
        public ulong label;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct OrenNayarDiffuseData
    {
        public Vec3 N;
        public Color3 albedo;
        public float roughness;
        public int energy_compensation;
        public ulong label;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TranslucentData
    {
        public Vec3 N;
        public Color3 albedo;
        public ulong label;
    }

    /// What every lobe of libbsdl has in the test renderer: a local frame, a roughness, and the adapter that turns world space calls into local ones
    public abstract class BsdlLobe : BSDF
    {
        protected Frame frame;
        private float m_roughness;

        protected BsdlLobe(float rough)
        {
            m_roughness = rough;
        }

        protected void set_roughness(float r)
        {
            m_roughness = r;
        }

        protected float roughness()
        {
            return m_roughness;
        }

        protected abstract Power albedo_impl();

        protected abstract BsdlSample eval_impl(Vec3 wo, Vec3 wi);

        protected abstract BsdlSample sample_impl(Vec3 wo, Vec3 rnd);

        /// The part of the light a layer on top of this lobe lets through
        public virtual Power filter_o(Vec3 wo)
        {
            return Power.ZERO();
        }

        public override Color3 get_albedo(Vec3 wo)
        {
            return albedo_impl().toRGB(0);
        }

        public override BsdfSample eval(Vec3 wo, Vec3 wi)
        {
            BsdlSample s = eval_impl(frame.local(wo), frame.local(wi));
            return new BsdfSample(wi, s.weight.toRGB(0), s.pdf, s.roughness);
        }

        public override BsdfSample sample(Vec3 wo, float rx, float ry, float rz)
        {
            BsdlSample s = sample_impl(frame.local(wo), new Vec3(rx, ry, rz));
            return new BsdfSample(frame.world(s.wi), s.weight.toRGB(0), s.pdf, s.roughness);
        }
    }

    /// The fresnel term of a conductor (complex IOR)
    public struct ConductorFresnel : IBsdlFresnel
    {
        private Power IOR, extinction;
        private float lambda_0;

        public ConductorFresnel(Power IOR, Power extinction, float lambda_0)
        {
            this.IOR = IOR;
            this.extinction = extinction;
            this.lambda_0 = lambda_0;
        }

        public Power eval(float cos_theta)
        {
            cos_theta = Bsdl.CLAMP(cos_theta, 0.0f, 1.0f);
            Power one = new Power(1.0f, lambda_0);
            Power cosTheta2 = new Power(cos_theta * cos_theta, lambda_0);
            Power sinTheta2 = one - cosTheta2;
            Power n = IOR;
            Power k = extinction;
            Power n2 = n * n;
            Power k2 = k * k;
            Power t0 = n2 - k2 - sinTheta2;
            Power a2plusb2 = Power.sqrt(t0 * t0 + 4 * n2 * k2);
            Power t1 = a2plusb2 + cosTheta2;
            Power a = Power.sqrt(0.5f * (a2plusb2 + t0));
            Power t2 = (2.0f * cos_theta) * a;
            Power rs = (t1 - t2) / (t1 + t2).clamped(Bsdl.FLOAT_MIN, Bsdl.BIG);
            Power t3 = cosTheta2 * a2plusb2 + sinTheta2 * sinTheta2;
            Power t4 = t2 * sinTheta2;
            Power rp = rs * (t3 - t4) / (t3 + t4).clamped(Bsdl.FLOAT_MIN, Bsdl.BIG);
            return 0.5f * (rp + rs).clamped(0, 2);
        }

        /// The average reflectance, a rational fit
        public Power avg()
        {
            const float a = -0.32775145f;
            const float b = 0.18346033f;
            const float c = 0.61146583f;
            const float d = -0.07785134f;
            Power result = new Power();
            for (int i = 0; i < 3; i++)
            {
                float x = IOR[i];
                float y = extinction[i];
                float p = a + b * x + c * y + d * x * y;
                result[i] = Bsdl.CLAMP(p / (1 + p), 0.0f, 1.0f);
            }
            return result;
        }
    }

    /// conductor_bsdf
    public sealed class MxConductor : BsdlLobe
    {
        private GGXDist dist;
        private ConductorFresnel fresnel;
        private float E_ms;

        public MxConductor(ConductorData data, Vec3 wo, bool backfacing, float path_roughness) : base(0.0f)
        {
            BsdfGlobals globals = new BsdfGlobals(wo, data.N, data.N, backfacing, path_roughness, 1.0f, 0);
            frame = new Frame(globals.visible_normal(data.N), data.U);
            float rx = Bsdl.CLAMP(data.roughness_x, Bsdl.EPSILON, 2.0f);
            float ry = Bsdl.CLAMP(data.roughness_y, Bsdl.EPSILON, 2.0f);
            float ax = Bsdl.stdmax(rx, ry);
            float ay = Bsdl.stdmin(rx, ry);
            float b = ay / ax;
            float aniso = (1 - b) / (1 + b);
            float rough = globals.regularize_roughness(MathF.Sqrt(ax / (1 + aniso)));
            float cosNO = frame.Z.dot(globals.wo);
            dist = new GGXDist(rough, aniso, rx < ry);
            fresnel = new ConductorFresnel(globals.wave(data.IOR), globals.wave(data.extinction), globals.lambda_0);
            TabulatedEnergyCurve curve = new TabulatedEnergyCurve(EnergyTables.MiniMicrofacetGGX, rough, 0.0f);
            E_ms = curve.Emiss_eval(cosNO);
            set_roughness(rough);
        }

        protected override Power albedo_impl()
        {
            return fresnel.avg();
        }

        protected override BsdlSample eval_impl(Vec3 wo, Vec3 wi)
        {
            BsdlSample s = Turquin.eval_turquin_microms_reflection(dist, fresnel, E_ms, wo, wi);
            s.roughness = roughness();
            return s;
        }

        protected override BsdlSample sample_impl(Vec3 wo, Vec3 rnd)
        {
            BsdlSample s = Turquin.sample_turquin_microms_reflection(dist, fresnel, E_ms, wo, rnd);
            s.roughness = roughness();
            return s;
        }
    }

    public interface IDielectricFresnel : IBsdlFresnel
    {
        float refraction_eta();
    }

    /// The fresnel term of a dielectric interface
    public struct DielectricFresnel : IDielectricFresnel
    {
        public float eta;
        public const float IOR_MIN = 1.001f;
        public const float IOR_MAX = 5.0f;

        public DielectricFresnel(float _eta, bool backside)
        {
            if (backside)
            {
                _eta = 1 / _eta;
            }
            eta = _eta >= 1 ? Bsdl.CLAMP(_eta, IOR_MIN, IOR_MAX) : Bsdl.CLAMP(_eta, 1 / IOR_MAX, 1 / IOR_MIN);
        }

        public Power eval(float c)
        {
            float g = (eta - 1.0f) * (eta + 1.0f) + c * c;
            if (g > 0)
            {
                g = MathF.Sqrt(g);
                float A = (g - c) / (g + c);
                float B = (c * (g + c) - 1) / (c * (g - c) + 1);
                return new Power(0.5f * A * A * (1 + B * B), 1);
            }
            return Power.UNIT();
        }

        public static DielectricFresnel from_table_index(float tx, bool backside)
        {
            float eta = Bsdl.LERP(Bsdl.SQR(tx), IOR_MIN, IOR_MAX);
            return new DielectricFresnel(eta, backside);
        }

        public float table_index()
        {
            float feta = eta;
            float seta = Bsdl.CLAMP(feta < 1 ? 1 / feta : feta, IOR_MIN, IOR_MAX);
            float x = (seta - IOR_MIN) * (1 / (IOR_MAX - IOR_MIN));
            return MathF.Sqrt(x);
        }

        public float refraction_eta()
        {
            return eta;
        }
    }

    /// The generalized Schlick fresnel
    public struct SchlickFresnel : IDielectricFresnel
    {
        private DielectricFresnel dielectric;
        private Power F0, F90;
        private float exponent;
        private float tir_cos;

        public SchlickFresnel(Power F0, Power F90, float exponent, float _eta, bool backfacing)
        {
            dielectric = new DielectricFresnel(_eta, backfacing);
            this.F0 = F0.clamped(0, 1);
            this.F90 = F90.clamped(0, 1);
            this.exponent = exponent;
            tir_cos = dielectric.eta >= 1 ? 0 : MathF.Sqrt(1 - Bsdl.SQR(dielectric.eta));
        }

        public float table_index()
        {
            return dielectric.table_index();
        }

        public float refraction_eta()
        {
            return dielectric.eta;
        }

        public Power eval(float c)
        {
            c = Bsdl.CLAMP(c, 0.0f, 1.0f);
            if (c < tir_cos)
            {
                return Power.UNIT();
            }
            return Bsdl.LERP(fmath.fast_safe_pow(1 - c, exponent), F0, F90);
        }
    }

    /// A GGX microfacet reflection and refraction at a dielectric interface, with energy compensation through the tables
    public struct DielectricBSDF<F> where F : struct, IDielectricFresnel
    {
        private GGXDist d;
        private F f;
        private float E_ms;
        private float lambda_0;
        private bool dorefr;

        public DielectricBSDF(GGXDist dist, F fresnel, float cosNO, float roughness, bool dorefr, float lambda_0)
        {
            d = dist;
            f = fresnel;
            this.lambda_0 = lambda_0;
            this.dorefr = dorefr;
            E_ms = 0;
            if (!dorefr)
            {
                TabulatedEnergyCurve curve = new TabulatedEnergyCurve(EnergyTables.MiniMicrofacetGGX, roughness, 0.0f);
                E_ms = curve.Emiss_eval(cosNO);
            }
        }

        private float reflection_probability(Power Fr)
        {
            return Fr.cliped_rgb(lambda_0).avg(lambda_0);
        }

        public BsdlSample eval(Vec3 wo, Vec3 wi)
        {
            if (!dorefr)
            {
                return Turquin.eval_turquin_microms_reflection(d, f, E_ms, wo, wi);
            }
            float cosNO = wo.z;
            float cosNI = wi.z;
            if (cosNI > 0)
            {
                Vec3 m = V3.normalized(wo + wi);
                float cosMO = m.dot(wo);
                if (cosMO <= 0)
                {
                    return new BsdlSample();
                }
                float D = d.D(m);
                float G1 = d.G1(wo);
                Power Fr = f.eval(cosMO);
                float P = reflection_probability(Fr);
                if (P <= 0)
                {
                    return new BsdlSample();
                }
                float D_refl_D = d.D_refl_D(wo, m);
                float D_refl = D_refl_D * D;
                Power output = Fr * (d.G2_G1(wi, wo) * G1 / (D_refl_D * P));
                float pdf = D_refl / (4.0f * cosNO) * P;
                return new BsdlSample(wi, output, pdf, 0);
            }
            else if (cosNI < 0)
            {
                Vec3 Ht = V3.normalized(f.refraction_eta() * wi + wo) * ((f.refraction_eta() > 1) ? -1 : 1);
                float cosHO = Ht.dot(wo);
                float cosHI = Ht.dot(wi);
                if (cosHO <= 0 || cosHI >= 0)
                {
                    return new BsdlSample();
                }
                Power Fr = f.eval(cosHO);
                Power Ft = Power.UNIT() - Fr;
                float Pt = 1 - reflection_probability(Fr);
                if (Ht.z <= 0 || Pt <= 0)
                {
                    return new BsdlSample();
                }
                float D = d.D(Ht);
                float G1 = d.G1(wo);
                float J = (-cosHI * cosHO * Bsdl.SQR(f.refraction_eta())) / (wo.z * Bsdl.SQR(cosHI * f.refraction_eta() + cosHO));
                float D_refl_D = d.D_refl_D(wo, Ht);
                float D_refl = D_refl_D * D;
                float pdf = D_refl * J * Pt;
                Power output = Ft * (d.G2_G1(new Vec3(wi.x, wi.y, -wi.z), wo) * G1 / (D_refl_D * Pt));
                return new BsdlSample(wi, output, pdf, 0);
            }
            else
            {
                return new BsdlSample();
            }
        }

        public BsdlSample sample(Vec3 wo, float randu, float randv, float randw)
        {
            if (!dorefr)
            {
                return Turquin.sample_turquin_microms_reflection(d, f, E_ms, wo, new Vec3(randu, randv, randw));
            }
            Vec3 m = d.sample_for_refl(wo, randu, randv);
            float cosMO = wo.dot(m);
            if (cosMO <= 0)
            {
                return new BsdlSample();
            }
            float P = reflection_probability(f.eval(cosMO));
            bool choose_reflect = randw < P;
            Vec3 wi = choose_reflect ? Bsdl.reflect(wo, m) : Bsdl.refract(wo, m, f.refraction_eta());
            if ((choose_reflect && wi.z <= 0) || (!choose_reflect && wi.z >= 0))
            {
                return new BsdlSample();
            }
            return eval(wo, wi);
        }
    }

    /// dielectric_bsdf
    public sealed unsafe class MxDielectric : BsdlLobe
    {
        private DielectricBSDF<DielectricFresnel> spec;
        private Power refl_tint;
        private Power refr_tint;
        private Power wo_absorption;
        private float E_ms;
        private bool dorefl;
        private bool dorefr;

        public MxDielectric(DielectricData data, Vec3 wo, bool backfacing, float path_roughness) : base(0.0f)
        {
            BsdfGlobals globals = new BsdfGlobals(wo, data.N, data.N, backfacing, path_roughness, 1.0f, 0);
            frame = new Frame(globals.visible_normal(data.N), data.U);
            refl_tint = globals.wave(data.refl_tint);
            refr_tint = globals.wave(data.refr_tint);
            wo_absorption = new Power(1.0f, globals.lambda_0);
            dorefl = Bsdl.MAX_RGB(data.refl_tint) > 0;
            dorefr = Bsdl.MAX_RGB(data.refr_tint) > 0;
            float rx = Bsdl.CLAMP(data.roughness_x, Bsdl.EPSILON, 2.0f);
            float ry = Bsdl.CLAMP(data.roughness_y, Bsdl.EPSILON, 2.0f);
            float ax = Bsdl.stdmax(rx, ry);
            float ay = Bsdl.stdmin(rx, ry);
            float b = ay / ax;
            float aniso = (1 - b) / (1 + b);
            float rough = globals.regularize_roughness(MathF.Sqrt(ax / (1 + aniso)));
            float cosNO = frame.Z.dot(globals.wo);
            float IOR = Bsdl.CLAMP(data.IOR, DielectricFresnel.IOR_MIN, DielectricFresnel.IOR_MAX);
            DielectricFresnel fresnel = new DielectricFresnel(globals.relative_eta(IOR), globals.backfacing);
            E_ms = 0;
            spec = new DielectricBSDF<DielectricFresnel>(new GGXDist(rough, aniso, rx < ry), fresnel, cosNO, rough, dorefr, globals.lambda_0);
            if (dorefl && !dorefr)
            {
                E_ms = new TabulatedEnergyCurve(EnergyTables.DielectricReflFront, rough, fresnel.table_index()).Emiss_eval(cosNO);
            }
            else if (dorefr)
            {
                if (!globals.backfacing)
                {
                    E_ms = new TabulatedEnergyCurve(EnergyTables.DielectricBothFront, rough, fresnel.table_index()).Emiss_eval(cosNO);
                }
                else
                {
                    E_ms = new TabulatedEnergyCurve(EnergyTables.DielectricBothBack, rough, fresnel.table_index()).Emiss_eval(cosNO);
                }
            }
            set_roughness(rough);
            if (Bsdl.MAX_RGB(data.absorption) > 0 && dorefl && !dorefr)
            {
                float sinNO2 = 1 - Bsdl.SQR(cosNO);
                float inveta2 = Bsdl.SQR(1 / fresnel.refraction_eta());
                float cos_p = MathF.Sqrt(1 - Bsdl.stdmin(1.0f, inveta2 * sinNO2));
                float dist = 1 / Bsdl.stdmax(cos_p, Bsdl.FLOAT_MIN);
                Power sigma_a = globals.wave(data.absorption);
                wo_absorption = new Power(fmath.fast_exp(-sigma_a[0] * dist), fmath.fast_exp(-sigma_a[1] * dist), fmath.fast_exp(-sigma_a[2] * dist));
            }
        }

        public override Power filter_o(Vec3 wo)
        {
            return !dorefr ? E_ms * wo_absorption : Power.ZERO();
        }

        protected override Power albedo_impl()
        {
            return !dorefr ? refl_tint * (1 - E_ms) : Power.UNIT();
        }

        private Power get_tint(float cosNI)
        {
            return cosNI > 0 ? refl_tint : refr_tint;
        }

        protected override BsdlSample eval_impl(Vec3 wo, Vec3 wi)
        {
            if (!dorefl && !dorefr)
            {
                return new BsdlSample();
            }
            BsdlSample s = spec.eval(wo, wi);
            if (dorefr)
            {
                s.weight *= 1 / Bsdl.stdmax(0.01f, 1 - E_ms);
            }
            s.weight *= get_tint(s.wi.z);
            s.roughness = roughness();
            return s;
        }

        protected override BsdlSample sample_impl(Vec3 wo, Vec3 rnd)
        {
            if (!dorefl && !dorefr)
            {
                return new BsdlSample();
            }
            BsdlSample s = spec.sample(wo, rnd.x, rnd.y, rnd.z);
            if (dorefr)
            {
                s.weight *= 1 / Bsdl.stdmax(0.01f, 1 - E_ms);
            }
            if (Bsdl.MAX_ABS_XYZ(s.wi) < Bsdl.EPSILON)
            {
                return new BsdlSample();
            }
            s.weight *= get_tint(s.wi.z);
            s.roughness = roughness();
            return s;
        }
    }

    /// generalized_schlick_bsdf
    public sealed unsafe class MxGeneralizedSchlick : BsdlLobe
    {
        private DielectricBSDF<SchlickFresnel> spec;
        private Power refl_tint;
        private Power refr_tint;
        private float E_ms;
        private bool dorefl;
        private bool dorefr;

        public MxGeneralizedSchlick(SchlickData data, Vec3 wo, bool backfacing, float path_roughness) : base(0.0f)
        {
            BsdfGlobals globals = new BsdfGlobals(wo, data.N, data.N, backfacing, path_roughness, 1.0f, 0);
            frame = new Frame(globals.visible_normal(data.N), data.U);
            refl_tint = globals.wave(data.refl_tint);
            refr_tint = globals.wave(data.refr_tint);
            dorefl = Bsdl.MAX_RGB(data.refl_tint) > 0;
            dorefr = Bsdl.MAX_RGB(data.refr_tint) > 0;
            float rx = Bsdl.CLAMP(data.roughness_x, Bsdl.EPSILON, 2.0f);
            float ry = Bsdl.CLAMP(data.roughness_y, Bsdl.EPSILON, 2.0f);
            float ax = Bsdl.stdmax(rx, ry);
            float ay = Bsdl.stdmin(rx, ry);
            float b = ay / ax;
            float aniso = (1 - b) / (1 + b);
            float rough = globals.regularize_roughness(MathF.Sqrt(ax / (1 + aniso)));
            float cosNO = frame.Z.dot(globals.wo);
            float avg_F0 = Bsdl.CLAMP(Bsdl.AVG_RGB(data.F0), 0.0f, 0.99f);
            float sqrt_F0 = MathF.Sqrt(avg_F0);
            float refraction_ior = (1 + sqrt_F0) / (1 - sqrt_F0);
            Power F0 = globals.wave(data.F0);
            Power F90 = globals.wave(data.F90);
            SchlickFresnel fresnel = new SchlickFresnel(F0, F90, data.exponent, refraction_ior, globals.backfacing);
            E_ms = 0;
            spec = new DielectricBSDF<SchlickFresnel>(new GGXDist(rough, aniso, rx < ry), fresnel, cosNO, rough, dorefr, globals.lambda_0);
            if (dorefl && !dorefr)
            {
                E_ms = new TabulatedEnergyCurve(EnergyTables.DielectricReflFront, rough, fresnel.table_index()).Emiss_eval(cosNO);
            }
            else if (dorefr)
            {
                if (!globals.backfacing)
                {
                    E_ms = new TabulatedEnergyCurve(EnergyTables.DielectricBothFront, rough, fresnel.table_index()).Emiss_eval(cosNO);
                }
                else
                {
                    E_ms = new TabulatedEnergyCurve(EnergyTables.DielectricBothBack, rough, fresnel.table_index()).Emiss_eval(cosNO);
                }
            }
            set_roughness(rough);
        }

        public override Power filter_o(Vec3 wo)
        {
            return !dorefr ? new Power(E_ms, 1) : Power.ZERO();
        }

        protected override Power albedo_impl()
        {
            return !dorefr ? refl_tint * (1 - E_ms) : Power.UNIT();
        }

        private Power get_tint(float cosNI)
        {
            return cosNI > 0 ? refl_tint : refr_tint;
        }

        protected override BsdlSample eval_impl(Vec3 wo, Vec3 wi)
        {
            if (!dorefl && !dorefr)
            {
                return new BsdlSample();
            }
            BsdlSample s = spec.eval(wo, wi);
            if (dorefr)
            {
                s.weight *= 1 / Bsdl.stdmax(0.01f, 1 - E_ms);
            }
            s.weight *= get_tint(s.wi.z);
            s.roughness = roughness();
            return s;
        }

        protected override BsdlSample sample_impl(Vec3 wo, Vec3 rnd)
        {
            if (!dorefl && !dorefr)
            {
                return new BsdlSample();
            }
            BsdlSample s = spec.sample(wo, rnd.x, rnd.y, rnd.z);
            if (dorefr)
            {
                s.weight *= 1 / Bsdl.stdmax(0.01f, 1 - E_ms);
            }
            if (Bsdl.MAX_ABS_XYZ(s.wi) < Bsdl.EPSILON)
            {
                return new BsdlSample();
            }
            s.weight *= get_tint(s.wi.z);
            s.roughness = roughness();
            return s;
        }
    }

    /// burley_diffuse_bsdf
    public sealed class MxBurleyDiffuse : BsdlLobe
    {
        private Power diff_albedo;
        private float diff_roughness;

        public MxBurleyDiffuse(BurleyDiffuseData data, Vec3 wo, bool backfacing, float path_roughness) : base(1.0f)
        {
            BsdfGlobals globals = new BsdfGlobals(wo, data.N, data.N, backfacing, path_roughness, 1.0f, 0);
            frame = new Frame(globals.visible_normal(data.N));
            diff_albedo = globals.wave(data.albedo);
            diff_roughness = Bsdl.CLAMP(data.roughness, 0.0f, 1.0f);
        }

        private static float fresnel(float cos_theta, float F90)
        {
            float x = Bsdl.CLAMP(1.0f - cos_theta, 0.0f, 1.0f);
            return Bsdl.LERP(Bsdl.pown5(x), 1.0f, F90);
        }

        protected override Power albedo_impl()
        {
            return diff_albedo;
        }

        protected override BsdlSample eval_impl(Vec3 wo, Vec3 wi)
        {
            if (wo.z <= 0.0f || wi.z <= 0.0f)
            {
                return new BsdlSample();
            }
            Vec3 H = wi + wo;
            if (Bsdl.MAX_ABS_XYZ(H) < Bsdl.EPSILON)
            {
                return new BsdlSample();
            }
            Vec3 Hn = V3.normalized(H);
            float cosHI = Bsdl.CLAMP(wi.dot(Hn), 0.0f, 1.0f);
            float cosNO = Bsdl.CLAMP(wo.z, 0.0f, 1.0f);
            float cosNI = Bsdl.CLAMP(wi.z, 0.0f, 1.0f);
            float F90 = 0.5f + 2.0f * diff_roughness * Bsdl.SQR(cosHI);
            float refL = fresnel(cosNI, F90);
            float refV = fresnel(cosNO, F90);
            float pdf = cosNI * Bsdl.ONEOVERPI;
            return new BsdlSample(wi, diff_albedo * (refL * refV), pdf, 1.0f);
        }

        protected override BsdlSample sample_impl(Vec3 wo, Vec3 rnd)
        {
            if (wo.z <= 0.0f)
            {
                return new BsdlSample();
            }
            Vec3 wi = Bsdl.sample_cos_hemisphere(rnd.x, rnd.y);
            return eval_impl(wo, wi);
        }
    }

    /// oren_nayar_diffuse_bsdf (the Oren Nayar of OSL's own closures maps to it too)
    public sealed class MxOrenNayarDiffuse : BsdlLobe
    {
        private static readonly float constant1_FON = 0.5f - 2.0f / (3.0f * Bsdl.PI);
        private static readonly float constant2_FON = 2.0f / 3.0f - 28.0f / (15.0f * Bsdl.PI);

        private Power diff_albedo;
        private float diff_roughness;
        private bool do_energy_compensation;

        public MxOrenNayarDiffuse(OrenNayarDiffuseData data, Vec3 wo, bool backfacing, float path_roughness) : base(1.0f)
        {
            BsdfGlobals globals = new BsdfGlobals(wo, data.N, data.N, backfacing, path_roughness, 1.0f, 0);
            frame = new Frame(globals.visible_normal(data.N));
            diff_albedo = globals.wave(data.albedo);
            diff_roughness = Bsdl.CLAMP(data.roughness, 0.0f, 1.0f);
            do_energy_compensation = data.energy_compensation != 0;
        }

        private float E_FON_analytic(float mu)
        {
            float sigma = diff_roughness;
            float AF = 1.0f / (1.0f + constant1_FON * sigma);
            float BF = sigma * AF;
            float Si = MathF.Sqrt(Bsdl.stdmax(0.0f, 1.0f - mu * mu));
            float G = Si * (fmath.fast_acos(mu) - Si * mu) + 2.0f * ((Si / Bsdl.stdmax(mu, 1e-7f)) * (1.0f - Si * Si * Si) - Si) * (1.0f / 3.0f);
            return AF + (BF * Bsdl.ONEOVERPI) * G;
        }

        protected override Power albedo_impl()
        {
            return diff_albedo;
        }

        protected override BsdlSample eval_impl(Vec3 wo, Vec3 wi)
        {
            float cosNI = Bsdl.CLAMP(wi.z, 0.0f, 1.0f);
            float cosNO = Bsdl.CLAMP(wo.z, 0.0f, 1.0f);
            if (cosNI <= 0.0f || cosNO <= 0.0f)
            {
                return new BsdlSample();
            }
            float cosIO = Bsdl.CLAMP(wo.dot(wi), -1.0f, 1.0f);
            float s = cosIO - cosNI * cosNO;
            float pdf = cosNI * Bsdl.ONEOVERPI;
            if (!do_energy_compensation)
            {
                float s2 = Bsdl.SQR(diff_roughness);
                float A = 1.0f - 0.50f * s2 / (s2 + 0.33f);
                float B = 0.45f * s2 / (s2 + 0.09f);
                float stinv = s > 0.0f ? s / Bsdl.stdmax(cosNI, cosNO) : 0.0f;
                float f_ss = A + B * stinv;
                return new BsdlSample(wi, diff_albedo * f_ss, pdf, 1.0f);
            }
            else
            {
                float AF = 1.0f / (1.0f + constant1_FON * diff_roughness);
                float stinv = s > 0.0f ? s / Bsdl.stdmax(cosNI, cosNO) : s;
                float f_ss = AF * (1.0f + diff_roughness * stinv);
                float EFo = E_FON_analytic(cosNO);
                float EFi = E_FON_analytic(cosNI);
                float avgEF = AF * (1.0f + constant2_FON * diff_roughness);
                Power rho_ms = new Power();
                for (int i = 0; i < 3; i++)
                {
                    rho_ms[i] = Bsdl.SQR(diff_albedo[i]) * avgEF / (1 - diff_albedo[i] * Bsdl.stdmax(0.0f, 1.0f - avgEF));
                }
                float f_ms = Bsdl.stdmax(1e-7f, 1.0f - EFo) * Bsdl.stdmax(1e-7f, 1.0f - EFi) / Bsdl.stdmax(1e-7f, 1.0f - avgEF);
                return new BsdlSample(wi, diff_albedo * f_ss + rho_ms * f_ms, pdf, 1.0f);
            }
        }

        protected override BsdlSample sample_impl(Vec3 wo, Vec3 rnd)
        {
            if (wo.z <= 0.0f)
            {
                return new BsdlSample();
            }
            Vec3 wi = Bsdl.sample_cos_hemisphere(rnd.x, rnd.y);
            return eval_impl(wo, wi);
        }
    }

    /// translucent_bsdf
    public sealed class MxTranslucent : BsdlLobe
    {
        private Power tint;

        public MxTranslucent(TranslucentData data, Vec3 wo, bool backfacing, float path_roughness) : base(1.0f)
        {
            BsdfGlobals globals = new BsdfGlobals(wo, data.N, data.N, backfacing, path_roughness, 1.0f, 0);
            frame = new Frame(globals.visible_normal(data.N));
            tint = globals.wave(data.albedo);
        }

        protected override Power albedo_impl()
        {
            return tint;
        }

        protected override BsdlSample eval_impl(Vec3 wo, Vec3 wi)
        {
            if (wi.z >= 0.0f)
            {
                return new BsdlSample();
            }
            return new BsdlSample(wi, tint, MathF.Abs(wi.z) * Bsdl.ONEOVERPI, 1.0f);
        }

        protected override BsdlSample sample_impl(Vec3 wo, Vec3 rnd)
        {
            Vec3 wi = Bsdl.sample_cos_hemisphere(rnd.x, rnd.y);
            wi.z = -wi.z;
            return eval_impl(wo, wi);
        }
    }

    /// The Conty Kulla sheen distribution (the version without the legacy shadowing term)
    public struct ContyKullaDist
    {
        public const float MIN_ROUGHNESS = 0.06f;
        private float a;

        public ContyKullaDist(float rough)
        {
            a = Bsdl.CLAMP(rough, MIN_ROUGHNESS, 1.0f);
        }

        public float roughness()
        {
            return a;
        }

        public float D(Vec3 Hr)
        {
            float cos_theta = Bsdl.CLAMP(Hr.z, 0.0f, 1.0f);
            float sin_theta = MathF.Sqrt(1.0f - Bsdl.SQR(cos_theta));
            return fmath.fast_safe_pow(sin_theta, 1 / a) * (2 + 1 / a) * 0.5f * Bsdl.ONEOVERPI;
        }

        public float G2(Vec3 wo, Vec3 wi)
        {
            float cosNI = Bsdl.stdmin(1.0f, wi.z);
            float cosNO = Bsdl.stdmin(1.0f, wo.z);
            return (cosNI * cosNO) / (cosNI + cosNO - cosNI * cosNO);
        }
    }

    /// The Conty Kulla microfacet sheen (ContyKullaSheenMTX)
    public struct ContyKullaSheenMTX
    {
        private ContyKullaDist d;

        public ContyKullaSheenMTX(float rough)
        {
            d = new ContyKullaDist(rough);
        }

        public float roughness()
        {
            return d.roughness();
        }

        public BsdlSample eval(Vec3 wo, Vec3 wi)
        {
            float cosNO = wo.z;
            float cosNI = wi.z;
            if (cosNI <= 1e-5f || cosNO <= 1e-5f)
            {
                return new BsdlSample();
            }
            float D = d.D(V3.normalized(wo + wi));
            if (D < 1e-6)
            {
                return new BsdlSample();
            }
            float G2 = d.G2(wo, wi);
            return new BsdlSample(wi, new Power(D * G2 * 0.5f * Bsdl.PI / cosNO, 1), 0.5f * Bsdl.ONEOVERPI, 0);
        }

        public BsdlSample sample(Vec3 wo, float randu, float randv, float randw)
        {
            Vec3 wi = Bsdl.sample_uniform_hemisphere(randu, randv);
            return eval(wo, wi);
        }

        /// A rational fit from the MaterialX project
        public float albedo(float cosNO)
        {
            Vec2f r = new Vec2f(13.67300f, 1.0f) + new Vec2f(-68.78018f, 61.57746f) * cosNO + new Vec2f(799.08825f, 442.78211f) * roughness()
                      + new Vec2f(-905.00061f, 2597.49308f) * cosNO * roughness() + new Vec2f(60.28956f, 121.81241f) * cosNO * cosNO
                      + new Vec2f(1086.96473f, 3045.55075f) * roughness() * roughness();
            return Bsdl.CLAMP(r.x / r.y, 0.0f, 1.0f);
        }
    }

    /// The multiple scattering sheen with linearly transformed cosines (Zeltner, Burley, Chiang 2022)
    public struct ZeltnerBurleySheen
    {
        // Linearly transformed cosine sampling, tabulated coefficients (not the fit)
        public const float MIN_ROUGHNESS = 0.02f;
        private const int ltc_res = 32;
        private float roughness;

        public ZeltnerBurleySheen(float rough)
        {
            roughness = Bsdl.CLAMP(rough, MIN_ROUGHNESS, 1.0f);
        }

        private static Vec3 param(int row, int col)
        {
            int i = (row * ltc_res + col) * 3;
            return new Vec3(BsdlLuts.ZeltnerBurleySheenParam[i], BsdlLuts.ZeltnerBurleySheenParam[i + 1], BsdlLuts.ZeltnerBurleySheenParam[i + 2]);
        }

        /// The LTC coefficients, bilinearly interpolated from the 32x32 table over roughness and the cosine of wo
        public Vec3 fetch_coeffs(float cosNO)
        {
            float row = Bsdl.CLAMP(roughness, 0.0f, Bsdl.ALMOSTONE) * (ltc_res - 1);
            float col = Bsdl.CLAMP(cosNO, 0.0f, Bsdl.ALMOSTONE) * (ltc_res - 1);
            float r = MathF.Floor(row);
            float c = MathF.Floor(col);
            float rf = row - r;
            float cf = col - c;
            int ri = (int)r;
            int ci = (int)c;
            Vec3 v1 = param(ri, ci);
            Vec3 v2 = param(ri, ci + 1);
            Vec3 v3 = param(ri + 1, ci);
            Vec3 v4 = param(ri + 1, ci + 1);
            return Bsdl.LERP(rf, Bsdl.LERP(cf, v1, v2), Bsdl.LERP(cf, v3, v4));
        }

        public float albedo(float cosNO)
        {
            return fetch_coeffs(cosNO).z;
        }

        public BsdlSample eval(Vec3 wo, Vec3 wi)
        {
            if (wo.z < 0 || wi.z <= 0)
            {
                return new BsdlSample();
            }
            return eval(wo, wi, fetch_coeffs(wo.z));
        }

        private BsdlSample eval(Vec3 wo, Vec3 wi, Vec3 ltc)
        {
            float a_inv = ltc.x;
            float b_inv = ltc.y;
            float r_coeff = ltc.z;
            Vec3 wi_orig = new Vec3(a_inv * wi.x + b_inv * wi.z, a_inv * wi.y, wi.z);
            float jacobian = Bsdl.SQR(a_inv / wi_orig.length2());
            float pdf = jacobian * Bsdl.stdmax(wi_orig.z, 0.0f) * Bsdl.ONEOVERPI;
            if (pdf > Bsdl.FLOAT_MIN)
            {
                return new BsdlSample(wi, new Power(r_coeff, 1), pdf, roughness);
            }
            return new BsdlSample();
        }

        public BsdlSample sample(Vec3 wo, float randu, float randv, float randw)
        {
            if (wo.z < 0)
            {
                return new BsdlSample();
            }
            Vec3 ltc = fetch_coeffs(wo.z);
            float a_inv = ltc.x;
            float b_inv = ltc.y;
            Vec3 wi_orig = Bsdl.sample_cos_hemisphere(randu, randv);
            Vec3 wi = new Vec3(wi_orig.x - wi_orig.z * b_inv, wi_orig.y, a_inv * wi_orig.z);
            return eval(wo, V3.normalized(wi), ltc);
        }
    }

    /// sheen_bsdf
    public sealed unsafe class MxSheen : BsdlLobe
    {
        private const int CONTY = 0;
        private const int ZELTNER = 1;

        private Power tint;
        private float sheen_alpha;
        private float Emiss;
        private int sheen_mode;
        private bool is_backfacing;

        public MxSheen(SheenData data, Vec3 wo, bool backfacing, float path_roughness) : base(0.0f)
        {
            BsdfGlobals globals = new BsdfGlobals(wo, data.N, data.N, backfacing, path_roughness, 1.0f, 0);
            frame = new Frame(globals.visible_normal(data.N), globals.wo);
            set_roughness(globals.regularize_roughness(Bsdl.CLAMP(data.roughness, 0.0f, 1.0f)));
            tint = globals.wave(data.albedo);
            Emiss = 1.0f;
            sheen_mode = data.mode;
            is_backfacing = globals.backfacing;
            sheen_alpha = use_zeltner() ? Bsdl.MAX(ZeltnerBurleySheen.MIN_ROUGHNESS, MathF.Sqrt(roughness())) : Bsdl.MAX(ContyKullaDist.MIN_ROUGHNESS, roughness());
            float cosNO = Bsdl.CLAMP(frame.Z.dot(globals.wo), 0.0f, 1.0f);
            if (is_backfacing)
            {
                Emiss = 1;
            }
            else if (use_zeltner())
            {
                ZeltnerBurleySheen sheen = new ZeltnerBurleySheen(sheen_alpha);
                Emiss = 1 - Bsdl.stdmin(sheen.albedo(cosNO) * tint.max(), 1.0f);
            }
            else
            {
                ContyKullaSheenMTX sheen = new ContyKullaSheenMTX(sheen_alpha);
                Emiss = 1 - Bsdl.stdmin(sheen.albedo(cosNO) * tint.max(), 1.0f);
            }
        }

        private bool use_zeltner()
        {
            return sheen_mode == ZELTNER;
        }

        public override Power filter_o(Vec3 wo)
        {
            return new Power(Emiss, 1);
        }

        protected override Power albedo_impl()
        {
            return tint * (1 - Emiss);
        }

        protected override BsdlSample eval_impl(Vec3 wo, Vec3 wi)
        {
            float cosNO = wo.z;
            float cosNI = wi.z;
            bool is_reflection = cosNI > 0 && cosNO >= 0;
            bool do_self = is_reflection && !is_backfacing;
            if (!do_self)
            {
                return new BsdlSample();
            }
            BsdlSample s = use_zeltner() ? new ZeltnerBurleySheen(sheen_alpha).eval(wo, wi) : new ContyKullaSheenMTX(sheen_alpha).eval(wo, wi);
            s.weight *= tint;
            s.roughness = roughness();
            return s;
        }

        protected override BsdlSample sample_impl(Vec3 wo, Vec3 rnd)
        {
            if (is_backfacing)
            {
                return new BsdlSample();
            }
            BsdlSample s = use_zeltner() ? new ZeltnerBurleySheen(sheen_alpha).sample(wo, rnd.x, rnd.y, rnd.z)
                                         : new ContyKullaSheenMTX(sheen_alpha).sample(wo, rnd.x, rnd.y, rnd.z);
            s.weight *= tint;
            s.roughness = roughness();
            return s;
        }
    }
}
