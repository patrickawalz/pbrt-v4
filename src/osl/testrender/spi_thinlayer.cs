// OSL port: the thin layer lobe of libbsdl (BSDL/SPI/bsdf_thinlayer_decl.h, bsdf_thinlayer_impl.h) as testrender's SpiThinLayer uses it
//
// A thin slab of dielectric (a glass pane, a soap film) modeled as a microfacet interface with the multiple internal bounces summed up in closed form,
// plus an energy compensating diffuse like lobe. It reflects and refracts, and absorbs light on the way through with sigma_t.

using System;
using System.Runtime.InteropServices;
using OIIO;

namespace OSL
{
    [StructLayout(LayoutKind.Sequential)]
    public struct ThinLayerData
    {
        public Vec3 N;
        public Vec3 T;
        public float IOR;
        public float roughness;
        public float anisotropy;
        public float thickness;
        public float prob_clamp;  // Not exposed
        public Color3 refl_tint;  // overall tint on reflected rays
        public Color3 refr_tint;  // overall tint on refracted rays
        public Color3 sigma_t;
    }

    /// The fresnel term of a thin layer (always from the outside, eta > 1)
    public struct ThinFresnel
    {
        public float eta;
        private const float IOR_MIN = 1.001f;
        private const float IOR_MAX = 5.0f;

        public ThinFresnel(float eta)
        {
            this.eta = Bsdl.CLAMP(eta, IOR_MIN, IOR_MAX);
        }

        public float eval(float c)
        {
            float g = MathF.Sqrt(eta * eta - 1 + c * c);
            float A = (g - c) / (g + c);
            float B = (c * (g + c) - 1) / (c * (g - c) + 1);
            return 0.5f * A * A * (1 + B * B);
        }

        public float eval_inv(float c)
        {
            return Bsdl.fresnel_dielectric(c, 1 / eta);
        }

        public float avgf()
        {
            return (eta - 1) / (4.08567f + 1.00071f * eta);
        }

        public float avg_invf()
        {
            return Bsdl.avg_fresnel_dielectric(1 / eta);
        }

        public static ThinFresnel from_table_index(float tx)
        {
            float eta = Bsdl.LERP(Bsdl.SQR(tx), IOR_MIN, IOR_MAX);
            return new ThinFresnel(eta);
        }

        public float table_index()
        {
            float seta = Bsdl.CLAMP(eta < 1 ? 1 / eta : eta, IOR_MIN, IOR_MAX);
            float x = (seta - IOR_MIN) * (1 / (IOR_MAX - IOR_MIN));
            return MathF.Sqrt(x);
        }
    }

    /// The microfacet interface of a thin layer (ThinMicrofacet<GGXDist> and Thinlayer)
    public struct Thinlayer
    {
        public const float AVG_INV_COS = 2.2f;

        public GGXDist d;
        public Power sigma_t;
        public ThinFresnel f;
        public float thickness;
        public float roughness;
        public float prob_clamp;

        public Thinlayer(float roughness, float aniso, float eta, float thickness, float prob_clamp, Power sigma_t)
        {
            d = new GGXDist(roughness, aniso, false);
            this.sigma_t = sigma_t;
            f = new ThinFresnel(eta);
            this.thickness = thickness;
            this.roughness = roughness;
            this.prob_clamp = prob_clamp;
        }

        public float eta()
        {
            return f.eta;
        }

        public static float sum_refl_series(float Rout, float Tin, float Rin, float A)
        {
            float b = Bsdl.SQR(Rin * A);  // Basis of the geometric series
            return Rout + (1 - b < Bsdl.EPSILON ? (A < 1 ? 0 : Tin * 0.5f) : Tin * (1 - Rin) * Bsdl.SQR(A) * Rin / (1 - b));
        }

        public static float sum_refr_series(float Rout, float Tin, float Rin, float A)
        {
            float b = Bsdl.SQR(Rin * A);  // Basis of the geometric series
            return 1 - b < Bsdl.EPSILON ? (A < 1 ? 0 : Tin * 0.5f) : Tin * (1 - Rin) * A / (1 - b);
        }

        public float fresnel_prob(float fr)
        {
            float safe_prob = 0.2f;
            return Bsdl.LERP(prob_clamp, fr, Bsdl.CLAMP(fr, safe_prob, 1 - safe_prob));
        }

        public float refraction_slope_scale(float cosNO)
        {
            cosNO = Bsdl.stdmin(cosNO, 1.0f);
            float inveta = 1 / eta();
            float sinNI = inveta * MathF.Sqrt(1 - Bsdl.SQR(cosNO));
            if (sinNI > 1.0f)
            {
                return 1;
            }
            float cosNI = -MathF.Sqrt(1 - Bsdl.SQR(sinNI));
            float refr_jacobian_entry = (1 + inveta * (cosNO / cosNI));
            float refr_jacobian_exit = (1 + eta() * (cosNI / cosNO));
            float refl_jacobian = 2;
            float refr_variance = Bsdl.SQR(refr_jacobian_entry) + Bsdl.SQR(refr_jacobian_exit);
            float refl_variance = Bsdl.SQR(refl_jacobian);
            float max_scale = 1 / roughness;
            return Bsdl.stdmin(MathF.Sqrt((refr_variance / refl_variance) / cosNO), max_scale);
        }

        public Vec3 scale_slopes(Vec3 m, float s)
        {
            return V3.normalized(new Vec3(m.x * s, m.y * s, m.z));
        }

        public void attenuation(Vec3 wo, Vec3 m, out Power refl, out Power refr)
        {
            Vec3 wr = Bsdl.refract(wo, m, eta());
            float cosNO = Bsdl.stdmin(wo.dot(m), 1.0f);
            float cosNR = Bsdl.CLAMP(-wr.z, 0.0f, 1.0f);
            float dist = thickness * Bsdl.LERP(roughness, 1 / Bsdl.stdmax(cosNR, 1e-6f), AVG_INV_COS);
            float Rout = f.eval(cosNO);
            float Tin = 1.0f - Rout;
            float Rin = Bsdl.LERP(roughness, f.eval_inv(cosNR), f.avg_invf());
            Power A = Power.UNIT();
            if (dist > 0)
            {
                for (int i = 0; i < 3; i++)
                {
                    A[i] = sigma_t[i] > 0 ? fmath.fast_exp(-dist * sigma_t[i]) : 1;
                }
            }
            refl = new Power();
            refr = new Power();
            for (int i = 0; i < 3; i++)
            {
                refl[i] = sum_refl_series(Rout, Tin, Rin, A[i]);
                refr[i] = sum_refr_series(Rout, Tin, Rin, A[i]);
            }
        }

        private BsdlSample eval(Vec3 wo, Vec3 m, Vec3 wi, bool both, float refr_slope_scale, Power refl_atten, Power refr_atten)
        {
            bool isrefl = wi.z > 0;
            float R = refl_atten.max();
            float T = refr_atten.max();
            float F = R / Bsdl.stdmax(R + T, Bsdl.FLOAT_MIN);
            float P = both ? fresnel_prob(isrefl ? F : 1 - F) : 1;
            if (P < Bsdl.PDF_MIN)
            {
                return new BsdlSample();
            }
            Vec3 wif = new Vec3(wi.x, wi.y, MathF.Abs(wi.z));
            float cosNO = wo.z;
            float cosNM = m.z;
            float sinNM = MathF.Sqrt(1 - Bsdl.stdmin(Bsdl.SQR(cosNM), 1.0f));
            float tmp = MathF.Sqrt(Bsdl.SQR(refr_slope_scale * sinNM) + Bsdl.SQR(cosNM));
            float J = isrefl ? 1 : tmp * tmp * tmp / Bsdl.SQR(refr_slope_scale);
            float D = d.D(m);
            float G1 = d.G1(wo);
            float output = d.G2_G1(wif, wo) / P;
            float pdf = (G1 * D * J * P) / (4.0f * cosNO);
            Power w = (isrefl ? refl_atten : refr_atten) * output;
            return new BsdlSample(wi, w, pdf, 0);
        }

        public BsdlSample eval(Vec3 wo, Vec3 wi, bool doreflect, bool dorefract)
        {
            bool both = doreflect && dorefract;
            bool isrefl = wi.z > 0;
            Vec3 wif = new Vec3(wi.x, wi.y, MathF.Abs(wi.z));  // Flipped reflection trick
            Vec3 mt = V3.normalized(wo + wif);
            float refr_slope_scale = refraction_slope_scale(wo.z);
            Vec3 m = isrefl ? mt : scale_slopes(mt, 1 / refr_slope_scale);
            if (wi.z == 0 || wo.z == 0 || m.dot(wo) <= 0 || mt.dot(wo) <= 0)
            {
                return new BsdlSample();
            }
            Power refl_atten;
            Power refr_atten;
            attenuation(wo, m, out refl_atten, out refr_atten);
            return eval(wo, m, wi, both, refr_slope_scale, refl_atten, refr_atten);
        }

        public BsdlSample sample(Vec3 wo, float randu, float randv, float randw, bool doreflect, bool dorefract)
        {
            bool both = doreflect && dorefract;
            Vec3 m = d.sample(wo, randu, randv);
            if (wo.dot(m) <= 0)
            {
                return new BsdlSample();
            }
            Power refl_atten;
            Power refr_atten;
            attenuation(wo, m, out refl_atten, out refr_atten);
            float R = refl_atten.max();
            float T = refr_atten.max();
            float F = R / Bsdl.stdmax(R + T, Bsdl.FLOAT_MIN);
            float P = both ? fresnel_prob(F) : (dorefract ? 0 : 1);
            bool isrefl = randw < P;
            float refr_slope_scale = refraction_slope_scale(wo.z);
            Vec3 mt = isrefl ? m : scale_slopes(m, refr_slope_scale);
            if (wo.dot(mt) <= 0)
            {
                return new BsdlSample();
            }
            Vec3 wif = Bsdl.reflect(wo, mt);
            Vec3 wi = isrefl ? wif : new Vec3(wif.x, wif.y, -wif.z);
            if ((isrefl && wi.z <= 0) || (!isrefl && wi.z >= 0))
            {
                return new BsdlSample();
            }
            return eval(wo, m, wi, both, refr_slope_scale, refl_atten, refr_atten);
        }
    }

    /// thinlayer (testrender's SpiThinLayer)
    public sealed class SpiThinLayer : BsdlLobe
    {
        private Thinlayer spec;
        private Power refl_tint;
        private Power refr_tint;
        private float Eo;

        public SpiThinLayer(ThinLayerData data, Vec3 wo, float path_roughness) : base(0.0f)
        {
            BsdfGlobals globals = new BsdfGlobals(wo, data.N, data.N, false, path_roughness, 1.0f, 0);
            frame = new Frame(globals.visible_normal(data.N), data.T);
            set_roughness(globals.regularize_roughness(Bsdl.CLAMP(data.roughness, 0.0f, 1.0f)));
            spec = new Thinlayer(roughness(), data.anisotropy, data.IOR, data.thickness, data.prob_clamp, globals.wave(data.sigma_t));
            refl_tint = globals.wave(data.refl_tint).clamped(0, 1);
            refr_tint = globals.wave(data.refr_tint).clamped(0, 1);
            TabulatedEnergyCurve diff_curve = new TabulatedEnergyCurve(EnergyTables.Thinlayer, roughness(), spec.f.table_index());
            float cosNO = Bsdl.CLAMP(globals.wo.dot(frame.Z), 0.0f, 1.0f);
            Eo = diff_curve.Emiss_eval(cosNO);
        }

        private void get_diff_trans(out Power diff_tint, out Power trans_tint)
        {
            float Tf = 1 - spec.f.avgf();
            float Tb = 1 - spec.f.avg_invf();
            float x = Tb * Bsdl.SQR(spec.eta()) / (Tf + Tb * Bsdl.SQR(spec.eta()));
            float Tout = Tb * (1 - x);
            float Tin = Tf * x;
            float Rout = 1 - Tin;
            float Rin = 1 - Tout;
            float avgd = spec.thickness * Thinlayer.AVG_INV_COS;
            Power A = new Power();
            for (int i = 0; i < 3; i++)
            {
                A[i] = fmath.fast_exp(-avgd * spec.sigma_t[i]);
            }
            diff_tint = new Power();
            trans_tint = new Power();
            for (int i = 0; i < 3; i++)
            {
                diff_tint[i] = Thinlayer.sum_refl_series(Rout, Tin, Rin, A[i]);
                trans_tint[i] = Thinlayer.sum_refr_series(Rout, Tin, Rin, A[i]);
            }
        }

        private Power get_tint(float cosNI)
        {
            return cosNI > 0 ? refl_tint : refr_tint;
        }

        private void eval_ec_lobe(ref BsdlSample s, Vec3 wi_l, Power diff_tint, Power trans_tint, float Tprob)
        {
            bool back = wi_l.z <= 0;
            float side_prob = back ? Tprob : 1 - Tprob;
            if (side_prob == 0)
            {
                return;
            }
            float dpdf = Eo * MathF.Abs(wi_l.z) * Bsdl.ONEOVERPI;
            Power w = back ? trans_tint : diff_tint;
            s.update(w, dpdf, side_prob);
        }

        private static float sum_max(float a, float b, float smax)
        {
            float maxab = Bsdl.stdmax(a, b);
            float minab = Bsdl.stdmin(a, b);
            return maxab + (smax - maxab) * (minab / smax);
        }

        private static Vec3 sample_lobe(float randu, float randv, bool back)
        {
            Vec3 wi = Bsdl.sample_cos_hemisphere(randu, randv);
            return back ? new Vec3(wi.x, wi.y, -wi.z) : wi;
        }

        protected override Power albedo_impl()
        {
            // The base class of testrender's thin layer adapter does not define an albedo, the BSDF default (white) is used
            return Power.UNIT();
        }

        protected override BsdlSample eval_impl(Vec3 wo, Vec3 wi)
        {
            return eval_impl(wo, wi, true, true);
        }

        protected override BsdlSample sample_impl(Vec3 wo, Vec3 rnd)
        {
            return sample_impl(wo, rnd, true, true);
        }

        private BsdlSample eval_impl(Vec3 wo, Vec3 wi, bool doreflect, bool dorefract)
        {
            bool both = doreflect && dorefract;
            float cosNI = wi.z;
            Power diff;
            Power trans;
            get_diff_trans(out diff, out trans);
            float R = diff.max();
            float T = trans.max();
            float Tprob = T / Bsdl.stdmax(R + T, Bsdl.FLOAT_MIN);
            if ((cosNI > 0 && !doreflect) || (cosNI < 0 && !dorefract) || cosNI == 0)
            {
                return new BsdlSample();
            }
            BsdlSample s = new BsdlSample();
            s.wi = wi;
            float PE = Eo * (both ? 1 : (dorefract ? Tprob : 1 - Tprob));
            BsdlSample ss = spec.eval(wo, wi, doreflect, dorefract);
            s.update(ss.weight, ss.pdf, 1 - PE);
            eval_ec_lobe(ref s, wi, diff, trans, Tprob);
            s.weight *= get_tint(cosNI);
            s.roughness = sum_max(roughness(), cosNI < 0 ? roughness() : 0, 1.0f);
            return s;
        }

        private BsdlSample sample_impl(Vec3 wo, Vec3 _rnd, bool doreflect, bool dorefract)
        {
            bool both = doreflect && dorefract;
            Power diff;
            Power trans;
            get_diff_trans(out diff, out trans);
            float R = diff.max();
            float T = trans.max();
            float Tprob = T / Bsdl.stdmax(R + T, Bsdl.FLOAT_MIN);
            float PE = Eo * (both ? 1 : (dorefract ? Tprob : 1 - Tprob));
            BsdlSample s = new BsdlSample();
            Vec3 rnd = _rnd;
            if (rnd.x < 1 - PE)
            {
                rnd.x = Stretch(rnd.x, 0.0f, 1 - PE);
                BsdlSample ss = spec.sample(wo, rnd.x, rnd.y, rnd.z, doreflect, dorefract);
                if (Bsdl.MAX_ABS_XYZ(ss.wi) == 0)
                {
                    return new BsdlSample();
                }
                s.wi = ss.wi;
                s.update(ss.weight, ss.pdf, 1 - PE);
                eval_ec_lobe(ref s, s.wi, diff, trans, Tprob);
            }
            else
            {
                rnd.x = Stretch(rnd.x, 1 - PE, PE);
                bool back = !(!dorefract || (both && rnd.z >= Tprob));
                s.wi = sample_lobe(rnd.x, rnd.y, back);
                eval_ec_lobe(ref s, s.wi, diff, trans, Tprob);
                BsdlSample ss = spec.eval(wo, s.wi, doreflect, dorefract);
                s.update(ss.weight, ss.pdf, 1 - PE);
            }
            float cosNI = s.wi.z;
            s.weight *= get_tint(cosNI);
            s.roughness = sum_max(roughness(), cosNI < 0 ? roughness() : 0, 1.0f);
            return s;
        }

        // Sample::stretch
        private static float Stretch(float x, float min, float length)
        {
            return Bsdl.stdmin((x - min) / length, Bsdl.ALMOSTONE);
        }
    }
}
