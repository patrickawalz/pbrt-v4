// (not in pbrt-v4) pearlescent_phase.cs
//
// PearlescentPhaseFunction: a microflake medium of thin-film platelets (Guillen et al., "A General Framework for
// Pearlescent Materials", ACM Trans. Graph. 39(6), 2020): the volume that PearlescentBxDF (bxdfs.cs) is the slab of.
//
// The medium collides at the gray rate kappaMaj >= kappa * Sigma(w); a collision hits a flake with probability
// s = kappa Sigma(w) / kappaMaj, the flake reflects with R(lambda) and otherwise the ray continues straight
// (null-forward). Reflection part:
//   p(wo, wi) = kappa D(m) R(lambda) / (4 kappaMaj),  m = normalize(wo + wi)
// Sampling draws m from the visible normals, reflects with probability q = s mean_lambda R, else continues forward (a
// delta event: PhaseFunctionSample.delta). Fully analytic, no tables.

using System;

namespace pbrt
{
    // What "string phase" "pearlescent" of a medium reads
    public sealed class PearlescentPhaseParameters
    {
        public PearlescentStack stack;
        public Float kappa, kappaMaj, alphaX, alphaY;
        public Vector3f axis;

        public static PearlescentPhaseParameters Create(ParameterDictionary parameters)
        {
            PearlescentPhaseParameters p = new PearlescentPhaseParameters();
            p.stack.containerEta = parameters.GetOneFloat("containern", 1.57f);
            p.stack.coatingEta = parameters.GetOneFloat("coatn", 2.5f);
            p.stack.coatingAbbe = parameters.GetOneFloat("coatabbe", 9f);
            p.stack.coatingThickness = parameters.GetOneFloat("coatthickness", 60f);
            p.stack.substrateEta = parameters.GetOneFloat("substraten", 1.58f);
            p.stack.substrateAbbe = parameters.GetOneFloat("substrateabbe", 63f);
            p.stack.substrateThickness = parameters.GetOneFloat("substratethickness", 500f);
            p.stack.substrateDeviation = parameters.GetOneFloat("substratestd", 60f);
            p.alphaX = std.max<Float>(parameters.GetOneFloat("uroughness", parameters.GetOneFloat("roughness", 0.2f)), 1e-3f);
            p.alphaY = std.max<Float>(parameters.GetOneFloat("vroughness", parameters.GetOneFloat("roughness", 0.2f)), 1e-3f);
            p.kappa = parameters.GetOneFloat("kappa", 3f);
            p.kappaMaj = p.kappa * std.max<Float>(1f, std.max(p.alphaX, p.alphaY));
            p.axis = parameters.GetOneVector3f("axis", new Vector3f(0, 0, 1));
            return p;
        }
    }

    // PearlescentPhaseFunction Definition
    public sealed unsafe class PearlescentPhaseFunction : PhaseFunction
    {
        public PearlescentPhaseFunction(PearlescentPhaseParameters parameters, SampledWavelengths lambda)
        {
            kappa = parameters.kappa;
            kappaMaj = parameters.kappaMaj;
            SampledSpectrum wavelengths = new SampledSpectrum(0f);
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                wavelengths[i] = lambda[i];
            }
            // The platelets of the slab: their distribution and their reflectance at the wavelengths
            slab = new PearlescentBxDF(parameters.stack, wavelengths, parameters.kappa, parameters.alphaX, parameters.alphaY, new SampledSpectrum(0f), 1, 1);
            Vector3f n = Normalize(parameters.axis);
            Vector3f a = std.abs(n.x) > 0.9f ? new Vector3f(0, 1, 0) : new Vector3f(1, 0, 0);
            t = Normalize(Cross(a, n));
            b = Cross(n, t);
            nrm = n;
        }

        public override bool WavelengthDependent() { return true; }

        // The values of the hero-wavelength interface: the mean over the wavelengths, which is what the directions are
        // sampled with
        public override Float p(Vector3f wo, Vector3f wi)
        {
            return pSpectral(wo, wi).Average();
        }

        public override SampledSpectrum pSpectral(Vector3f wo, Vector3f wi)
        {
            Vector3f a = ToLocal(wo), c = ToLocal(wi);
            Vector3f hv = a + c;
            Float len2 = LengthSquared(hv);
            if (len2 < 1e-12f)
            {
                return new SampledSpectrum(0f);
            }
            Vector3f mm = hv / std.sqrt(len2);
            if (mm.z < 0)
            {
                mm = -mm;
            }
            Float cs = std.min<Float>(1, AbsDot(a, mm));
            Float scale = kappa * slab.D(mm) / (4 * kappaMaj);
            return scale * slab.Rhat(cs);
        }

        public override Float PDF(Vector3f wo, Vector3f wi)
        {
            Vector3f a = ToLocal(wo), c = ToLocal(wi);
            Vector3f hv = a + c;
            Float len2 = LengthSquared(hv);
            if (len2 < 1e-12f)
            {
                return 0;
            }
            Vector3f mm = hv / std.sqrt(len2);
            if (mm.z < 0)
            {
                mm = -mm;
            }
            Float cs = std.min<Float>(1, AbsDot(a, mm));
            Float sg = slab.Sigma(a);
            Float q = std.min<Float>(1, kappa * sg / kappaMaj * slab.Rhat(cs).Average());
            return q * slab.D(mm) / (4 * sg);
        }

        public override pstd.optional<PhaseFunctionSample> Sample_p(Vector3f wo, Point2f u)
        {
            Vector3f a = ToLocal(wo);
            Vector3f mm = slab.SampleM(a, u[0], u[1]);
            Float cs = std.min<Float>(1, AbsDot(a, mm));
            Float sg = slab.Sigma(a);
            Float s = kappa * sg / kappaMaj;
            SampledSpectrum R = slab.Rhat(cs);
            Float q = std.min<Float>(1, s * R.Average());
            double r = (double)(Hash(wo.x, wo.y, wo.z, u[0], u[1]) >> 11) * (1.0 / 9007199254740992.0);
            PhaseFunctionSample sample = new PhaseFunctionSample();
            sample.hasSpec = true;
            if (r < q)
            {
                Vector3f ri = mm * (2 * Dot(a, mm)) - a;
                sample.wi = FromLocal(ri);
                Float d = slab.D(mm) / (4 * sg);
                sample.spec = s * d * R;
                sample.pdf = q * d;
                sample.p = sample.pdf;
                if (!(sample.pdf > 0))
                {
                    return default;
                }
            }
            else
            {
                sample.wi = -wo;
                sample.spec = new SampledSpectrum(1f) - s * R;
                sample.pdf = 1 - q;
                sample.p = sample.pdf;
                sample.delta = true;
                if (!(sample.pdf > 0))
                {
                    return default;
                }
            }
            return sample;
        }

        // The Mueller matrix of the scattering from wi to wo with the scalar phase function divided out (its first element is
        // 1): the reflection by the platelet whose normal is the half vector, the thin films averaged over the thickness of the
        // substrate as for PearlescentBxDF. A ray that goes on undisturbed keeps its polarisation. Each reflection of a path
        // in the medium has its matrix: nothing is taken as depolarising.
        public PolarisedLightSystem.Attenuation MuellerMatrix(Vector3f wo, Vector3f wi, bool fromCamera)
        {
            Vector3f hv = wo + wi;
            Float len2 = LengthSquared(hv);
            if (len2 < 1e-12f)
            {
                return PolarisedLightSystem.Attenuation.Identity();
            }
            return slab.PlateletMatrix(wo, wi, hv / std.sqrt(len2), fromCamera);
        }

        public override string ToString()
        {
            return StringPrintf("[ PearlescentPhaseFunction kappa: %f kappaMaj: %f ]", kappa, kappaMaj);
        }

        private Vector3f ToLocal(Vector3f v)
        {
            return new Vector3f(Dot(v, t), Dot(v, b), Dot(v, nrm));
        }

        private Vector3f FromLocal(Vector3f v)
        {
            return Normalize(t * v.x + b * v.y + nrm * v.z);
        }

        // PearlescentPhaseFunction Private Members
        private PearlescentBxDF slab;
        private Float kappa, kappaMaj;
        private Vector3f t, b, nrm;
    }
}
