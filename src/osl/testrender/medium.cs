// OSL port: the participating media of the test renderer (MediumParams, MediumStack in testrender/shading.h, the Henyey Greenstein phase function of
// BSDL/SPI/bsdf_volume_impl.h, and process_medium_closure of testrender/shading.cpp)
//
// A path keeps a stack of the media it is inside. Every surface the ray crosses with a refractive MaterialX closure pushes or pops one; between two
// surfaces a ray inside a scattering medium samples a distance and may scatter.

using System;
using OIIO;

namespace OSL
{
    /// The properties of a medium (initialized to vacuum)
    public sealed class MediumParams
    {
        public Color3 sigma_t = new Color3(0.0f);  // extinction coefficient
        public Color3 sigma_s = new Color3(0.0f);  // scattering
        public float medium_g = 0.0f;  // volumetric anisotropy
        public float refraction_ior = 1.0f;
        public int priority = 0;

        public MediumParams()
        {
        }

        public MediumParams(MediumParams o)
        {
            sigma_t = o.sigma_t;
            sigma_s = o.sigma_s;
            medium_g = o.medium_g;
            refraction_ior = o.refraction_ior;
            priority = o.priority;
        }

        public bool is_vaccum()
        {
            return sigma_t.x <= 0.0f && sigma_t.y <= 0.0f && sigma_t.z <= 0.0f;
        }

        public bool is_special_priority()
        {
            return priority == 0;
        }

        /// Samples a new direction from the Henyey Greenstein phase function of the medium
        public BsdfSample sample_phase_func(Vec3 wo, float rx, float ry, float rz)
        {
            if (is_vaccum())
            {
                return new BsdfSample(new Vec3(1.0f), new Color3(1.0f), 0.0f, 0.0f);
            }
            HenyeyGreenstein phase_func = new HenyeyGreenstein(medium_g, medium_g, 0.0f, wo);
            return phase_func.sample(wo, rx, ry, rz);
        }
    }

    /// The Henyey Greenstein phase function (BSDL VolumeLobe), possibly a blend of two
    public sealed class HenyeyGreenstein : BsdlLobe
    {
        private float m_g1, m_g2, m_blend;

        public HenyeyGreenstein(float g1, float g2, float blend, Vec3 wo) : base(1.0f)
        {
            frame = new Frame(-wo);
            m_g1 = Bsdl.CLAMP(g1, -0.99f, 0.99f);
            m_g2 = Bsdl.CLAMP(g2, -0.99f, 0.99f);
            m_blend = blend;
        }

        private static float phase_func(float costheta, float g)
        {
            if (g == 0)
            {
                return 0.25f * Bsdl.ONEOVERPI;
            }
            float num = 0.25f * Bsdl.ONEOVERPI * (1 - g * g);
            float den = 1 + g * g + 2.0f * g * costheta;
            return num / MathF.Sqrt(den * den * den);
        }

        private static float phase_func(float costheta, float g1, float g2, float blend)
        {
            float p1 = phase_func(costheta, g1);
            float p2 = phase_func(costheta, g2);
            return Bsdl.LERP(blend, p1, p2);
        }

        private static Vec3 sample_phase(float g1, float g2, float blend, Vec2f sample)
        {
            float g;
            float x;
            if (sample.x < blend)
            {
                g = g2;
                x = sample.x / blend;
            }
            else
            {
                g = g1;
                x = (sample.x - blend) / (1 - blend);
            }
            float cosTheta;
            if (MathF.Abs(g) < 1e-3f)
            {
                cosTheta = 1 - 2 * x;
            }
            else
            {
                float k = (1 - g * g) / (1 - g + 2 * g * x);
                cosTheta = (1 + g * g - k * k) / (2 * g);
            }
            float sinTheta = MathF.Sqrt(Bsdl.MAX(0.0f, 1.0f - cosTheta * cosTheta));
            float phi = 2 * sample.y;
            float cosPhi = fmath.fast_cospi(phi);
            float sinPhi = fmath.fast_sinpi(phi);
            return new Vec3(sinTheta * cosPhi, sinTheta * sinPhi, cosTheta);
        }

        protected override Power albedo_impl()
        {
            return Power.UNIT();
        }

        protected override BsdlSample eval_impl(Vec3 wo, Vec3 wi)
        {
            float OdotI = Bsdl.CLAMP(-wi.z, -1.0f, 1.0f);
            float pdf = phase_func(OdotI, m_g1, m_g2, m_blend);
            return new BsdlSample(wi, Power.UNIT(), pdf, 1.0f);
        }

        protected override BsdlSample sample_impl(Vec3 wo, Vec3 rnd)
        {
            Vec3 wi = sample_phase(m_g1, m_g2, m_blend, new Vec2f(rnd.x, rnd.y));
            return eval_impl(wo, wi);
        }
    }

    /// The media a path is in, sorted by priority, and the properties they add up to
    public sealed class MediumStack
    {
        private const int MaxEntries = 8;

        private MediumParams[] pool = new MediumParams[MaxEntries];  // stable storage
        private MediumParams[] mediums = new MediumParams[MaxEntries];  // sorted by descending priority
        private MediumParams[] entry_order = new MediumParams[MaxEntries];  // LIFO order (identity)
        private float[] cdf = new float[MaxEntries];
        private int[] overlapping_medium_indices = new int[MaxEntries];
        private MediumParams current_params = new MediumParams();
        private int depth;
        private int pool_size;
        private int num_overlapping;

        public MediumParams get_current_params()
        {
            if (depth > 0)
            {
                return mediums[0];
            }
            return null;
        }

        public bool in_medium()
        {
            return depth > 0;
        }

        public int size()
        {
            return depth;
        }

        private void compute_current_params()
        {
            // compute data for overlapping mediums, called if a new medium was added to the stack
            MediumParams new_params = new MediumParams();
            num_overlapping = 0;
            for (int i = 0; i < depth; i++)
            {
                MediumParams params_i = mediums[i];
                if (i == 0)
                {
                    // current params has to have the same priority as mediums[0]
                    new_params.priority = params_i.priority;
                }
                if (params_i.priority != new_params.priority)
                {
                    continue;
                }
                overlapping_medium_indices[num_overlapping] = i;
                new_params.sigma_t += params_i.sigma_t;
                new_params.sigma_s += params_i.sigma_s;
                float avg_sigma_s_i = (params_i.sigma_s.x + params_i.sigma_s.y + params_i.sigma_s.z) / 3.0f;
                cdf[num_overlapping] = (num_overlapping > 0 ? cdf[num_overlapping - 1] : 0.0f) + avg_sigma_s_i;
                num_overlapping++;
            }
            if (num_overlapping > 1 && !new_params.is_vaccum())
            {
                float total_cdf = cdf[num_overlapping - 1];
                if (total_cdf > 0.0f)
                {
                    for (int i = 0; i < num_overlapping; i++)
                    {
                        cdf[i] /= total_cdf;
                    }
                }
            }
            // clamp sigma_s to be less than sigma_t
            new_params.sigma_s = new Color3(Bsdl.stdmin(new_params.sigma_s.x, new_params.sigma_t.x), Bsdl.stdmin(new_params.sigma_s.y, new_params.sigma_t.y),
                                            Bsdl.stdmin(new_params.sigma_s.z, new_params.sigma_t.z));
            current_params = new_params;
        }

        private static Color3 transmittance(Color3 sigma_t, float distance)
        {
            // Beer-Lambert law
            return new Color3(MathF.Exp(-sigma_t.x * distance), MathF.Exp(-sigma_t.y * distance), MathF.Exp(-sigma_t.z * distance));
        }

        /// Lets the ray scatter inside the medium before it reaches the surface. Returns true if it did (r is the scattered ray then)
        public bool integrate(Ray r, Sampler sampler, ref Intersection hit, ref Color3 path_weight, ref Color3 path_radiance, ref float bsdf_pdf)
        {
            if (depth <= 0)
            {
                return false;
            }
            if (current_params.is_vaccum())
            {
                return false;
            }
            Vec3 weighted_sigma_s = path_weight * current_params.sigma_s / current_params.sigma_t;
            float channel_weights0 = weighted_sigma_s.x;
            float channel_weights1 = weighted_sigma_s.y;
            float channel_weights2 = weighted_sigma_s.z;
            float total = channel_weights0 + channel_weights1 + channel_weights2;
            if (total <= 0.0f)
            {
                // pure absorption: apply transmittance to the surface and continue
                Color3 absorbed = transmittance(current_params.sigma_t, hit.t);
                path_weight *= absorbed;
                return false;
            }
            float inv_total = 1.0f / total;
            channel_weights0 *= inv_total;
            channel_weights1 *= inv_total;
            channel_weights2 *= inv_total;

            // Used for channel selection, distance, and cdf sampling
            Vec3 rand = sampler.get();

            // pick a channel using the cdf of channel_weights
            int channel;
            float rand_channel = rand.y;
            if (rand_channel < channel_weights0)
            {
                channel = 0;
            }
            else if (rand_channel < channel_weights0 + channel_weights1)
            {
                channel = 1;
            }
            else
            {
                channel = 2;
            }

            // sample distance along the ray using the selected channel's sigma_t
            float sigma_t_channel = current_params.sigma_t[channel];
            float t_volume = -MathF.Log(1.0f - rand.x) / sigma_t_channel;
            bool scatter = t_volume < hit.t;
            float t = scatter ? t_volume : hit.t;
            Color3 tr = transmittance(current_params.sigma_t, t);
            Color3 density = scatter ? (current_params.sigma_t * tr) : tr;
            float pdf = density.x * channel_weights0 + density.y * channel_weights1 + density.z * channel_weights2;
            if (pdf <= 0.0f)
            {
                return false;
            }
            if (scatter)
            {
                path_weight *= (tr * current_params.sigma_s) / pdf;
            }
            else
            {
                path_weight *= (tr / pdf);
                return false;
            }

            // scattering picks a new direction
            r.origin = r.point(t_volume);
            int index = 0;
            if (num_overlapping > 1)
            {
                for (index = 0; index < num_overlapping - 1; ++index)
                {
                    if (rand.z < cdf[index])
                    {
                        break;
                    }
                }
            }
            int medium_index = overlapping_medium_indices[index];
            Vec3 rand_phase = sampler.get();
            BsdfSample phase_sample = mediums[medium_index].sample_phase_func(-r.direction, rand_phase.x, rand_phase.y, rand_phase.z);
            if (phase_sample.pdf > 0.0f)
            {
                path_weight *= phase_sample.weight;
                r.direction = phase_sample.wi;
                bsdf_pdf = phase_sample.pdf;
                return true;
            }
            return false;
        }

        public bool add_medium(MediumParams new_params)
        {
            if (depth >= MaxEntries || pool_size >= MaxEntries)
            {
                return false;
            }
            MediumParams p = new MediumParams(new_params);
            pool[pool_size++] = p;

            // find insertion point (sorted by descending priority)
            int insert_pos = depth;
            for (int i = 0; i < depth; ++i)
            {
                if (new_params.priority > mediums[i].priority)
                {
                    insert_pos = i;
                    break;
                }
            }

            // shift entries to make room
            for (int j = depth; j > insert_pos; --j)
            {
                mediums[j] = mediums[j - 1];
            }
            mediums[insert_pos] = p;
            entry_order[depth] = p;
            depth++;
            compute_current_params();
            return true;
        }

        public void pop_medium()
        {
            if (depth <= 0)
            {
                return;
            }
            depth--;

            // find the most recently added medium
            MediumParams p = entry_order[depth];
            int sorted_index = -1;
            for (int i = 0; i <= depth; ++i)
            {
                if (ReferenceEquals(mediums[i], p))
                {
                    sorted_index = i;
                    break;
                }
            }
            if (sorted_index < 0)
            {
                return;  // shouldn't happen
            }

            // shift entries down to fill the gap
            for (int j = sorted_index; j < depth; ++j)
            {
                mediums[j] = mediums[j + 1];
            }

            // reclaim pool if this was the last allocated entry
            if (ReferenceEquals(p, pool[pool_size - 1]))
            {
                pool_size--;
            }
            compute_current_params();
        }

        /// A surface that is crossed inside a medium of a higher priority is no real boundary
        public bool false_intersection_with(MediumParams entrant_params)
        {
            MediumParams current = get_current_params();
            if (current == null)
            {
                return false;
            }

            // special priority will always record an intersection
            if (entrant_params.is_special_priority() && current.is_special_priority())
            {
                return false;
            }

            // same non-special priority: never record an intersection
            if (entrant_params.priority == current.priority)
            {
                return true;
            }
            return entrant_params.priority > current.priority;
        }
    }
}
