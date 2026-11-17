// (not in pbrt-v4) polarisedgather.cs
//
// The polarisation of the light that the production path tracer and its volumes gather. The scalar path is left as it is:
// every strategy still returns the spectrum that it adds to the light of the path. A strategy that knows how its light was
// scattered also records the same quantity here as polarised light: the Stokes vector of the light as it leaves the point
// of the path towards the vertex before, in a frame of that direction. When the path adds the spectrum, it takes what was
// recorded as the polarised part of it and the rest as unpolarised light, so a strategy that records nothing has the right
// intensity and no polarisation, and the scalar path cannot be changed by what is recorded.
//
// The recorded light has the scale of what the strategy returns; a caller that scales the returned spectrum scales what
// was recorded since a mark by the same factor (Here, ScaleSince).

using PLight = pbrt.PolarisedLightSystem.Light;
using PAttenuation = pbrt.PolarisedLightSystem.Attenuation;

namespace pbrt
{
    public sealed class PolarisedGather
    {
        public PLight light = PLight.Zero();
        public SampledSpectrum scalar = new SampledSpectrum(0.0f);
        // The light and the point on it of the last light sample that the renderer gave a volume (for the polarisation of what a
        // glowing surface emits); no light: unpolarised
        public SampledEmitter emitter;

        public struct Mark
        {
            public PLight light;
            public SampledSpectrum scalar;
        }

        public void Reset()
        {
            light = PLight.Zero();
            scalar = new SampledSpectrum(0.0f);
        }

        public void Add(SampledSpectrum value, PLight polarised)
        {
            light += polarised;
            scalar += value;
        }

        public Mark Here()
        {
            Mark mark;
            mark.light = light;
            mark.scalar = scalar;
            return mark;
        }

        public void ScaleSince(Mark mark, SampledSpectrum factor)
        {
            light = mark.light + (light - mark.light) * factor;
            scalar = mark.scalar + (scalar - mark.scalar) * factor;
        }

        public void ScaleSince(Mark mark, Float factor)
        {
            ScaleSince(mark, new SampledSpectrum(factor));
        }

        // What was recorded since the mark is dropped (a strategy whose result is not used)
        public void DropSince(Mark mark)
        {
            light = mark.light;
            scalar = mark.scalar;
        }

        // The polarised form of `total`, which is all that was gathered since the last time: what was recorded, and the rest as
        // unpolarised light
        public PLight Take(SampledSpectrum total)
        {
            PLight result = light + new PLight(total - scalar);
            Reset();
            return result;
        }

        // What the light of the last light sample emits along -wi, with the intensity `value`
        public PLight Emitted(Vector3f wi, SampledWavelengths lambda, SampledSpectrum value)
        {
            if (emitter.light == null)
            {
                return new PLight(value);
            }
            // (a light that a ray reached gave its radiance by the hit, a sampled light by the point: the same way here)
            SurfaceInteraction reached = emitter.point as SurfaceInteraction;
            if (reached != null && reached.areaLight == emitter.light)
            {
                return PolarisedEmission.AtHit(reached, -wi, lambda, value);
            }
            return PolarisedEmission.AtPoint(emitter.light, emitter.point, -wi, lambda, value);
        }

        // `value`: the light of the last light sample, arriving along -wi, scattered by the phase function towards wo
        public void Scattered(SampledSpectrum value, PhaseFunction phase, Vector3f wo, Vector3f wi, SampledWavelengths lambda)
        {
            if (!(bool)value)
            {
                return;
            }
            // (a scattering that depolarises leaves unpolarised light, which is what the rest is taken as)
            if (!PolarisedPathIntegrator.PhaseAttenuation(phase, wo, wi, true, out PAttenuation matrix))
            {
                return;
            }
            Add(value, matrix * Emitted(wi, lambda, value));
        }

        // `value`: light that arrives unpolarised along -wi (the emission of a medium), scattered by the phase function towards wo
        public void ScatteredUnpolarised(SampledSpectrum value, PhaseFunction phase, Vector3f wo, Vector3f wi)
        {
            if (!(bool)value)
            {
                return;
            }
            if (!PolarisedPathIntegrator.PhaseAttenuation(phase, wo, wi, true, out PAttenuation matrix))
            {
                return;
            }
            Add(value, matrix * new PLight(value));
        }

        // `value`: the light of the last light sample, arriving along -wi at a second point, scattered there by `second` towards
        // -between, and at the first point by `first` (the matrix of the first scattering from `between` towards the vertex
        // before, over its first element; not known: it depolarises)
        public void ScatteredTwice(SampledSpectrum value, bool firstKnown, PAttenuation first, PhaseFunction second, Vector3f between, Vector3f wi,
                                   SampledWavelengths lambda)
        {
            if (!(bool)value || !firstKnown)
            {
                return;
            }
            PLight arriving = Emitted(wi, lambda, value);
            if (PolarisedPathIntegrator.PhaseAttenuation(second, -between, wi, true, out PAttenuation matrix))
            {
                arriving = matrix * arriving;
            }
            else
            {
                arriving = new PLight(value);
            }
            Add(value, first * arriving);
        }

        // The matrix, over its first element, of a phase function that scatters light arriving along -wi towards wo
        public static bool PhaseMatrix(PhaseFunction phase, Vector3f wo, Vector3f wi, out PAttenuation matrix)
        {
            return PolarisedPathIntegrator.PhaseAttenuation(phase, wo, wi, true, out matrix);
        }
    }
}
