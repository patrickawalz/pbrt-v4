// (not in pbrt-v4) cpu/pathstate.cs
//
// The throughput of a path and its updates, shared by the integrators that walk paths (volpath, bdpt). Each integrator keeps
// its own loop and its own order of random numbers; what is common is how the throughput changes at a null collision, at a real
// scattering in a medium, at the end of a segment, at a Russian roulette and at a coat that the path passes through. The
// polarised state mirrors every scalar factor of the throughput, so these updates are written once for both.

using System;
using static pbrt.pbrt;
using PLight = pbrt.PolarisedLightSystem.Light;
using PAttenuation = pbrt.PolarisedLightSystem.Attenuation;

namespace pbrt
{

    // PathState Definition
    public struct PathState
    {
        // The scalar throughput, and the rescaled probabilities r_u (unidirectional) and r_l (light sampling) of volpath
        public SampledSpectrum beta;
        public SampledSpectrum r_u;
        public SampledSpectrum r_l;
        // The Mueller matrix of the throughput of a camera path, and the Stokes vector of the throughput of a light path
        public PAttenuation pa;
        public PLight pl;
        public bool fromCamera;
        public bool polarised;
        // Whether r_u and r_l are tracked (volpath) or not (bdpt keeps the densities in its vertices)
        public bool rescaled;
        // (bdpt) The null collisions of the segment that the walk is in, for the spectral MIS weights of its vertices: segU is the
        // ratio of the probability of the segment, as a walk samples it, to the hero wavelength's, and segL the same for the segment
        // as a connection samples it (the shadow ray of a light sample), with the hero wavelength's walk probability as the
        // denominator of both: these are the increments of volpath's r_u and r_l along the segment. vtx is the same for the real
        // scatterings in media at the vertex (the extinction of each wavelength over the hero's).
        public bool trackSegment;
        public SampledSpectrum segU, segL, vtx;

        public PathState(SampledSpectrum beta, PAttenuation pa, PLight pl, bool fromCamera, bool polarised, bool rescaled)
        {
            this.beta = beta;
            this.r_u = new SampledSpectrum(1.0f);
            this.r_l = new SampledSpectrum(1.0f);
            this.pa = pa;
            this.pl = pl;
            this.fromCamera = fromCamera;
            this.polarised = polarised;
            this.rescaled = rescaled;
            this.trackSegment = false;
            this.segU = new SampledSpectrum(1.0f);
            this.segL = new SampledSpectrum(1.0f);
            this.vtx = new SampledSpectrum(1.0f);
        }

        // The segment of a new vertex starts with no collisions
        public void ResetSegment()
        {
            segU = new SampledSpectrum(1.0f);
            segL = new SampledSpectrum(1.0f);
            vtx = new SampledSpectrum(1.0f);
        }

        // A scalar factor of the throughput
        public void Scale(SampledSpectrum w)
        {
            beta *= w;
            ScalePolarised(w);
        }

        // The same factor on the polarised state alone
        private void ScalePolarised(SampledSpectrum w)
        {
            if (!polarised)
            {
                return;
            }
            if (fromCamera)
            {
                pa *= w;
            }
            else
            {
                pl *= w;
            }
        }

        // The Mueller matrix of a scattering (a camera path applies it to what comes later, a light path to what came before)
        public void Apply(PAttenuation M)
        {
            if (!polarised)
            {
                return;
            }
            if (fromCamera)
            {
                pa *= M;
            }
            else
            {
                pl = M * pl;
            }
        }

        // A null collision: the throughput and, for volpath, both rescaled probabilities; for a path that keeps its densities in
        // vertices (bdpt), the null collisions of the segment. False if the path is over.
        public bool NullCollision(SampledSpectrum T_maj, SampledSpectrum sigma_maj, SampledSpectrum sigma_n)
        {
            Float pdf = T_maj[0] * sigma_n[0];
            if (rescaled)
            {
                SampledSpectrum w = T_maj * sigma_n / pdf;
                beta *= w;
                if (pdf != 0)
                {
                    ScalePolarised(w);
                }
                if (pdf == 0)
                {
                    beta = new SampledSpectrum(0.0f);
                }
                r_u *= T_maj * sigma_n / pdf;
                r_l *= T_maj * sigma_maj / pdf;
                if (trackSegment && pdf != 0)
                {
                    segU *= T_maj * sigma_n / pdf;
                    segL *= T_maj * sigma_maj / pdf;
                }
                return (bool)beta && (bool)r_u;
            }
            if (pdf == 0)
            {
                beta = new SampledSpectrum(0.0f);
            }
            else
            {
                Scale(T_maj * sigma_n / pdf);
                if (trackSegment)
                {
                    segU *= T_maj * sigma_n / pdf;
                    segL *= T_maj * sigma_maj / pdf;
                }
            }
            return (bool)beta;
        }

        // A real scattering in a medium, chosen with the density of the hero wavelength
        public void RealScatter(SampledSpectrum T_maj, SampledSpectrum sigma_s)
        {
            SampledSpectrum w = T_maj * sigma_s / (T_maj[0] * sigma_s[0]);
            Scale(w);
            if (rescaled)
            {
                r_u *= w;
            }
            if (trackSegment)
            {
                // The distance to the collision is in the segment, and the scattering coefficient at the vertex
                segU *= T_maj / T_maj[0];
                segL *= T_maj / T_maj[0];
                vtx *= sigma_s / sigma_s[0];
            }
        }

        // The transmittance from the last collision to the end of the segment
        public void EndSegment(SampledSpectrum T_maj)
        {
            Scale(T_maj / T_maj[0]);
            if (rescaled)
            {
                r_u *= T_maj / T_maj[0];
                r_l *= T_maj / T_maj[0];
            }
            if (trackSegment)
            {
                segU *= T_maj / T_maj[0];
                segL *= T_maj / T_maj[0];
            }
        }

        // The scattering of a sampled BSDF direction at a surface: the scalar weight, and the Mueller matrix, which smooth
        // dielectrics and conductors polarise and every other scattering depolarises
        public void ScatterSurface(BSDF bsdf, SurfaceInteraction isect, Vector3f wo, BSDFSample bs, TransportMode mode)
        {
            SampledSpectrum w = bs.f * AbsDot(bs.wi, isect.shading.n) / bs.pdf;
            beta *= w;
            if (polarised)
            {
                if (!PolarisedPathIntegrator.SpecularAttenuation(bsdf, isect, wo, bs, mode, out PAttenuation A) &&
                    !PolarisedPathIntegrator.RoughAttenuation(bsdf, isect, wo, bs.wi, w, mode, out A))
                {
                    A = PAttenuation.Lambert(w);
                }
                Apply(A);
            }
        }

        // The scattering of a sampled phase function direction in a medium: only the Rayleigh part polarises
        public void ScatterPhase(PhaseFunction phase, Vector3f wo, BSDFSample ps)
        {
            SampledSpectrum w = ps.f / ps.pdf;
            beta *= w;
            if (polarised)
            {
                if (!PolarisedPathIntegrator.PhaseAttenuation(phase, wo, ps.wi, fromCamera, out PAttenuation M))
                {
                    M = PAttenuation.Lambert(new SampledSpectrum(1.0f));
                }
                M *= w;
                Apply(M);
            }
        }

        // The weight of a path that survives a Russian roulette that ended it with probability q
        public void SurviveRoulette(Float q)
        {
            beta /= 1 - q;
            ScalePolarised(new SampledSpectrum(1 / (1 - q)));
        }
    }

}  // namespace pbrt
