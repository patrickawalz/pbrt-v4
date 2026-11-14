// (not in pbrt-v4, not in the fork) polygrain_polarised.cs
//
// The Mueller matrices of the polydisperse micrograin BSDF. A grain reflects at its surface as a mirror of its material (the
// Fresnel matrix of the conductor or of the dielectric, in the plane of the ray and the normal of the grain) or diffusely (a
// depolariser); the bulk has the matrix of its own BxDF where it has one, and depolarises otherwise. As for the other walks of
// this branch (util/facetforge_polarised.cs), the matrix of a path is the product of those of its events over their
// probabilities, what a walk sends to wi is that product with the matrix of the last event, and the matrix returned is the sum
// over the walks divided by the scalar value of the same walks.

using System;
using Attenuation = pbrt.PolarisedLightSystem.Attenuation;

namespace pbrt
{
    public sealed unsafe partial class PolyGrainBxDF
    {
        private sealed class GrainPolar
        {
            public BSDF bsdf;
            // Whether the direction the walk starts from is that of the camera
            public bool fromCamera;
            public Attenuation chain, sum;
            public bool hasChain, hasSum;
            private Vector3f exit, enter;

            public GrainPolar(BSDF bsdf, bool fromCamera, Vector3f start, Vector3f other)
            {
                this.bsdf = bsdf;
                this.fromCamera = fromCamera;
                Vector3f fix = bsdf.LocalToRender(start), gen = bsdf.LocalToRender(other);
                exit = fromCamera ? fix : gen;
                enter = fromCamera ? -gen : -fix;
            }

            // One more event on the path
            public void Follow(Attenuation next)
            {
                chain = PolarisedFacet.Compose(chain, hasChain, next, fromCamera);
                hasChain = true;
            }

            // What an event sends to the other direction, after the events of the path (or alone)
            public void Add(Attenuation m, bool afterTheChain)
            {
                Attenuation t = PolarisedFacet.Compose(chain, afterTheChain && hasChain, m, fromCamera);
                if (!t.WithFrames(exit, enter) || t.HasNaNs())
                {
                    return;
                }
                if (hasSum)
                {
                    sum += t;
                }
                else
                {
                    sum = t;
                    hasSum = true;
                }
            }

            public void Scale(SampledSpectrum s)
            {
                if (hasSum)
                {
                    sum = sum * s;
                }
            }

            public void Merge(GrainPolar other)
            {
                if (!other.hasSum)
                {
                    return;
                }
                if (hasSum)
                {
                    sum += other.sum;
                }
                else
                {
                    sum = other.sum;
                    hasSum = true;
                }
            }
        }

        // The mirror reflection of a grain of the type given, from a to b about the normal n (its first element is the Fresnel
        // reflectance of unpolarised light)
        private Attenuation GrainMatrix(GrainPolar pol, int type, Vector3f a, Vector3f b, Vector3f n)
        {
            if (spec[type] == PolyGrainSpecular.Conductor)
            {
                return PolarisedFacet.Conductor(pol.bsdf, 1, a, b, n, eta[type], k[type], pol.fromCamera);
            }
            Float scalar;
            return PolarisedFacet.Dielectric(pol.bsdf, 1, a, b, n, AbsDot(a, n), eta[type][0], true, pol.fromCamera, out scalar);
        }

        // The bulk from a to b, with the first element `value`
        private Attenuation BulkMatrix(GrainPolar pol, Vector3f a, Vector3f b, TransportMode mode, SampledSpectrum value)
        {
            if (bulk.HasMuellerMatrix())
            {
                bool polarised;
                Attenuation A;
                bulk.f(pol.bsdf, a, b, mode, out polarised, out A);
                if (polarised)
                {
                    return A * value;
                }
            }
            return Attenuation.Lambert(value);
        }

        // The first scattering of the paper, by its parts
        private void FirstMatrices(GrainPolar pol, Vector3f wo, Vector3f wi, TransportMode mode, SampledSpectrum bulkPart, SampledSpectrum grains)
        {
            if ((bool)bulkPart)
            {
                pol.Add(BulkMatrix(pol, wo, wi, mode, bulkPart), false);
            }
            if (!(wi.z > 0) || !(bool)grains)
            {
                return;
            }
            SampledSpectrum specular = new SampledSpectrum(0f);
            Vector3f h = wo + wi;
            Float length = Length(h);
            if (length > 0)
            {
                h /= length;
                mg.Vec3d hd = V3(h), od = V3(wo), id = V3(wi);
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
                    Float factor = (Float)(dist.tauBar0 * pj * dist.GTilde(j, od, id, hd)) / (4 * h.z * AbsCosTheta(wo) * AbsCosTheta(wi));
                    pol.Add(GrainMatrix(pol, j, wo, wi, h) * new SampledSpectrum(factor), false);
                    specular += factor * GrainFresnel(j, AbsDot(wo, h));
                }
            }
            SampledSpectrum diffuse = ClampZero(grains - specular);
            if ((bool)diffuse)
            {
                pol.Add(Attenuation.Lambert(diffuse), false);
            }
        }

        public override bool HasMuellerMatrix() { return true; }

        public override SampledSpectrum f(BSDF bsdf, Vector3f wo, Vector3f wi, TransportMode mode, out bool polarised, out Attenuation matrix)
        {
            if (maxOrder == 1 && !(wo.z > 0))
            {
                // from below with one scattering: the bulk alone
                polarised = false;
                matrix = default;
                SampledSpectrum value = f(wo, wi, mode);
                if (bulk.HasMuellerMatrix())
                {
                    bulk.f(bsdf, wo, wi, mode, out polarised, out matrix);
                }
                return value;
            }
            return WalkF(bsdf, wo, wi, mode, true, out polarised, out matrix);
        }

        public override pstd.optional<BSDFSample> Sample_f(BSDF bsdf, Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags,
                                                           out bool polarised, out Attenuation matrix)
        {
            polarised = false;
            matrix = default;
            if (maxOrder > 1)
            {
                return WalkSample(bsdf, wo, uc, u, mode, sampleFlags, true, out polarised, out matrix);
            }
            pstd.optional<BSDFSample> bs = Sample_f(wo, uc, u, mode, sampleFlags);
            if (!bs)
            {
                return bs;
            }
            if (bs.value().IsSpecular())
            {
                // the mirror of the bulk through the pores: the matrix of the bulk, sampled with the same numbers
                if (bulk.HasMuellerMatrix())
                {
                    bool allowR = (sampleFlags & BxDFReflTransFlags.Reflection) != 0;
                    Float pB = (wo.z > 0 && allowR) ? BulkProbability(wo) : 1;
                    Float ub = pB > 0 ? std.min<Float>(uc / pB, OneMinusEpsilon) : 0;
                    bulk.Sample_f(bsdf, wo, ub, u, mode, sampleFlags, out polarised, out matrix);
                }
                return bs;
            }
            WalkF(bsdf, wo, bs.value().wi, mode, true, out polarised, out matrix);
            return bs;
        }
    }
}
