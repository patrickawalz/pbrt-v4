// (not in pbrt-v4) Bragg mirrors: materials whose colour is that of a one-dimensional photonic crystal, a cell of thin films
// repeated many times (the shells of jewel beetles, dichroic filters). After Fourneau, Pacanowski and Barla, "Interactive
// Exploration of Vivid Material Iridescence using Bragg Mirrors" (Computer Graphics Forum 2024).
//
// The reflectance of the mirror is the closed form of FrBraggPolarised() (fresnel_polarised.cs): Yeh's formula, which the paper
// brings to rendering, here for any cell, on any substrate and with its phases. Every material with thin films takes
// "integer layerrepeat" for it ("thinfilm" and the microsurfaces: thinfilmlayers.cs).
//
// This file has the layered structure of the paper, the material "bragg":
//
//   Material "bragg" "float layereta" [ 1 1.5 ] "float layerthickness" [ 125 375 ] "integer layerrepeat" 20
//       "float roughness" 0.1 "spectrum reflectance" ...
//
//   air | (a smooth surface of the host, if its index is not one) | host | rough Bragg layer | host | diffuse base
//
// The Bragg layer is a microsurface whose facets are Bragg mirrors, in a host medium of the same index on both of its sides
// ("float hosteta", default 1). A facet reflects as a mirror, and what it does not reflect (or absorb) goes through it; because
// the index is the same on both sides it goes straight on, whatever the facet's normal: the transmission is ballistic (the
// paper's section 4.2.1). By default the light meets facet after facet until it leaves the layer (BraggLayerBxDF, below);
// "string scattering" "single" is the model of the paper, one facet, which loses the light that the shadowing takes.
//
// The layers are then put together by the position-free random walk that every layered material of the renderer uses
// (StackBxDF, pbrt's LayeredBxDF), with a clear host between them: all the reflections between the Bragg layers, the base
// and the surface of the host are in it. The paper's faster model, which keeps one reflection only, is not used.
//
//   "float layerk" (or spectra)   the extinction coefficients of the films, one for each: films that absorb
//   "integer mirrorfilms", "integer mirrorrepeat"   several mirrors, the first on top: the number of films of the cell of
//                             each, and its number of cells. Each is a layer of its own, over the ones after it.
//   "string scattering"       "multiple" (default) or "single"; "integer scatterdepth" (64): the facets a walk may meet
//   "string base" "diffuse"   the base under the layer ("spectrum reflectance", default 0.05: a dark base shows the colours),
//                 "none"      or nothing: the Bragg layer alone, a sheet that reflects some colours and lets the others through
//   "float hosteta"           the index of the host medium (1)
//   "float roughness", "uroughness", "vroughness", "bool remaproughness", "string distribution", "float gamma"   as "conductor"
//   "integer maxdepth", "integer nsamples"   of the random walk between the layers (1000000: it ends by roulette; 1)
//   "bool polarisedinside"        (false) asked without polarisation, the walk still keeps the polarisation of the light
//                                 between its events and returns what unpolarised light gives

using System;
using System.Collections.Generic;
using Attenuation = pbrt.PolarisedLightSystem.Attenuation;

namespace pbrt
{
    // BraggLayerBxDF Definition
    // A microsurface whose facets are Bragg mirrors, in a host medium of the same index on both of its sides. A facet reflects
    // the light as a mirror or lets it through, and what goes through goes straight on.
    //
    // By default the microsurface is walked as in Heitz, Hanika, d'Eon and Dachsbacher, "Multiple-Scattering Microfacet BSDFs
    // with the Smith Model" (2016): the light meets facet after facet, above the surface or below it, until it leaves, so
    // nothing is lost to the shadowing (every facet shares the light between its reflection and its transmission: the layer
    // returns all of it). Light that only went through facets leaves in its own direction: the ballistic transmission, a
    // direction with a weight, which a sample is. Light that was reflected at least once leaves anywhere, above or below:
    // that part has a value, estimated by the walk that starts against wo, with, at every facet it meets, what that facet
    // would reflect to wi and what the facets on the way out along wi let through (a second walk that is never reflected).
    // The value is an estimate: unbiased, and the same for the same pair of directions. The density is estimated in the same
    // way, with the probabilities of the walk in the place of the reflectances.
    //
    // `single`: the model of the paper, one facet only. Its reflection is the microfacet BRDF with the reflectance of the
    // mirror, its transmission the transmittance of one facet that the direction sees, times the shadowing of the direction;
    // it loses the light that the shadowing takes, and it is not polarised.
    public sealed unsafe class BraggLayerBxDF : BxDF
    {
        public BraggLayerBxDF() { }

        public BraggLayerBxDF(ThinFilmStack film, Float hostEta, MicrofacetDistribution mfDistrib, bool single = false, int maxOrder = 64)
        {
            this.film = film;
            this.hostEta = hostEta;
            this.mfDistrib = mfDistrib;
            this.single = single;
            this.maxOrder = maxOrder;
            this.height = new MicrosurfaceHeightUniform();
        }

        public override BxDFFlags Flags()
        {
            if (mfDistrib.EffectivelySmooth())
            {
                return BxDFFlags.SpecularReflection | BxDFFlags.SpecularTransmission;
            }
            if (single)
            {
                return BxDFFlags.GlossyReflection | BxDFFlags.SpecularTransmission;
            }
            return BxDFFlags.GlossyReflection | BxDFFlags.GlossyTransmission | BxDFFlags.SpecularTransmission;
        }

        public override bool HasMuellerMatrix() { return true; }

        // The reflectance R and the transmittance T of a facet for unpolarised light that meets it with the cosine `cosine`
        // (positive) from above the layer or from below: their sum is one unless the films absorb. Returns the probability
        // with which a walk is reflected, the share of the reflectance in what the facet returns, averaged over the
        // wavelengths.
        private Float Mirror(Float cosine, bool above, out SampledSpectrum R, out SampledSpectrum T)
        {
            return Mirror(cosine, above, new SampledSpectrum(1f), out R, out T);
        }

        // For the lobes of a stack of layers (LayerStackBxDF.Lobes()): the mean reflectance and transmittance of the mirror
        // for light at the cosine `cosine` from above or from below, and the roughness of the layer
        public void Means(Float cosine, bool above, out Float reflectance, out Float transmittance)
        {
            Mirror(cosine, above, out SampledSpectrum R, out SampledSpectrum T);
            reflectance = R.Average();
            transmittance = T.Average();
        }

        public Float Alpha()
        {
            return SafeSqrt(mfDistrib.AlphaX() * mfDistrib.AlphaY());
        }

        // The same for a walk that carries `weight` at each wavelength: the probability is the share of the reflectance in
        // what the facet returns of it. What the walk carries, added over the wavelengths, then never grows: no wavelength
        // can come out of the layer with more than NSpectrumSamples times its share.
        private Float Mirror(Float cosine, bool above, SampledSpectrum weight, out SampledSpectrum R, out SampledSpectrum T)
        {
            film.Embedded(above ? cosine : -cosine, hostEta, out SampledSpectrum Fs, out SampledSpectrum Fp, out SampledSpectrum _, out SampledSpectrum _,
                          out SampledSpectrum _, out SampledSpectrum _, out SampledSpectrum Ts, out SampledSpectrum Tp);
            R = 0.5f * (Fs + Fp);
            T = 0.5f * (Ts + Tp);
            Float reflected = (weight * R).Average(), transmitted = (weight * T).Average();
            if (!(reflected + transmitted > 0) || IsInf(reflected + transmitted))
            {
                reflected = R.Average();
                transmitted = T.Average();
            }
            return reflected + transmitted > 0 ? Clamp(reflected / (reflected + transmitted), 0, 1) : 0;
        }

        // The probability with which a walk with polarisation is reflected by a facet of normal m that it meets from a (b: where
        // the reflection goes): as Mirror(), but by what the facet reflects and lets through of the light of this path, whose
        // matrix so far is `before`, in place of the means over the two polarisations. R and T are those means, as the walk
        // carries them.
        private Float Polarised(BSDF bsdf, Float side, Vector3f a, Vector3f b, Vector3f m, Float cosine, bool above, SampledSpectrum weight,
                                Attenuation before, bool fromCamera, out SampledSpectrum R, out SampledSpectrum T)
        {
            Float plain = Mirror(cosine, above, weight, out R, out T);
            Attenuation reflecting, passing;
            bool reflects = Facet(bsdf, side, a, b, m, cosine, above, true, fromCamera, out reflecting);
            bool passes = Facet(bsdf, side, a, -a, m, cosine, above, false, fromCamera, out passing);
            if (!reflects || !passes)
            {
                return plain;
            }
            SampledSpectrum withReflection = PolarisedFacet.Compose(before, true, reflecting, fromCamera).GetMuellerMatrix().Get(0, 0);
            SampledSpectrum withPassage = PolarisedFacet.Compose(before, true, passing, fromCamera).GetMuellerMatrix().Get(0, 0);
            Float reflected = 0, transmitted = 0;
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                // (what the path carries at the wavelength is its scalar weight times the first element of its matrix, and the
                // matrices of the facet have a first element of 1: the means R and T times the first element of the path with
                // the facet are what it carries afterwards)
                reflected += weight[i] * R[i] * std.max<Float>(0, withReflection[i]);
                transmitted += weight[i] * T[i] * std.max<Float>(0, withPassage[i]);
            }
            if (!(reflected + transmitted > 0) || IsInf(reflected + transmitted))
            {
                return plain;
            }
            // (kept away from 0 and 1 where the means are not: the first element of a part of a path says little about the rest)
            Float probability = reflected / (reflected + transmitted);
            return Clamp(probability, plain > 0 ? 0.02f : 0, plain < 1 ? 0.98f : 1);
        }

        // The matrix of a facet of normal m met from a that reflects the light to b or lets it through (b is then -a), with a
        // first element of 1 at every wavelength. False when the facet does not do it at any.
        private bool Facet(BSDF bsdf, Float side, Vector3f a, Vector3f b, Vector3f m, Float cosine, bool above, bool reflect, bool fromCamera,
                           out Attenuation matrix)
        {
            matrix = default;
            film.Embedded(above ? cosine : -cosine, hostEta, out SampledSpectrum Fs, out SampledSpectrum Fp, out SampledSpectrum Ds, out SampledSpectrum Dp,
                          out SampledSpectrum DsT, out SampledSpectrum DpT, out SampledSpectrum Ts, out SampledSpectrum Tp);
            SampledSpectrum scale = new SampledSpectrum(0f);
            bool any = false;
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                Float norm = reflect ? 0.5f * (Fs[i] + Fp[i]) : 0.5f * (Ts[i] + Tp[i]);
                scale[i] = norm > 0 ? 1 / norm : 0;
                any |= norm > 0;
            }
            if (!any)
            {
                return false;
            }
            matrix = reflect ? Attenuation.Fresnel(scale, Fs, Fp, Ds, Dp) : Attenuation.Fresnel(scale, Ts, Tp, DsT, DpT);
            matrix = matrix.Scattered(bsdf.LocalToRender(side * a), bsdf.LocalToRender(side * b), bsdf.LocalToRender(side * m), fromCamera);
            return true;
        }

        // Below the microsurface the walk is the mirror image of a walk above
        private Float NextHeight(Vector3f wr, Float h, bool outside, Float u)
        {
            if (outside)
            {
                return MicrosurfaceWalk.SampleHeight(mfDistrib, height, wr, h, u);
            }
            return -MicrosurfaceWalk.SampleHeight(mfDistrib, height, -wr, -h, u);
        }

        private Vector3f SampleNormal(Vector3f incoming, bool outside, ref RNG rng)
        {
            Point2f u = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
            return outside ? mfDistrib.Sample_wm_SingleSided(incoming, u) : -mfDistrib.Sample_wm_SingleSided(-incoming, u);
        }

        // What the facets let through along w from the height h on the side `outside`, until the light has left the layer: a
        // walk that no facet reflects. `through` is the product of the transmittances of the facets it met, `probability` the
        // product of the probabilities with which a walk that carries `weight` goes through them. With `keep` their matrices are added to `chain`.
        private void Escape(BSDF bsdf, Float side, Vector3f w, Float h, bool outside, bool flipped, bool fromCamera, bool keep, ref RNG rng,
                            SampledSpectrum weight, out SampledSpectrum through, out Float probability, ref Attenuation chain, ref bool hasChain)
        {
            through = new SampledSpectrum(1f);
            probability = 1;
            for (int order = 0; order < maxOrder; ++order)
            {
                h = NextHeight(w, h, outside, rng.Uniform<Float>());
                if (h == MicrosurfaceWalk.Left || h == -MicrosurfaceWalk.Left)
                {
                    return;
                }
                Vector3f incoming = -w;
                Vector3f wm = SampleNormal(incoming, outside, ref rng);
                Float cosine = Dot(incoming, wm);
                SampledSpectrum R, T;
                Float reflectance = Mirror(cosine, outside != flipped, weight * through, out R, out T);
                through *= T;
                probability *= 1 - reflectance;
                if (IsNaN(h) || IsNaN(cosine) || !(bool)through)
                {
                    break;
                }
                if (keep)
                {
                    Attenuation step;
                    if (!Facet(bsdf, side, incoming, w, wm, cosine, outside != flipped, false, fromCamera, out step))
                    {
                        break;
                    }
                    chain = PolarisedFacet.Compose(chain, hasChain, step, fromCamera);
                    hasChain = true;
                }
                outside = !outside;
            }
            through = new SampledSpectrum(0f);
            probability = 0;
        }

        // One walk from wo: `sum` estimates the power per solid angle that the facets reflect to wi (f cos(theta_i) of the part
        // that has a value), and `density` the density of wi among the directions in which a walk that was reflected leaves.
        // With `keep`, the sum of the matrices of the paths times what they add, and `polarised`.
        private void Walk(BSDF bsdf, Vector3f wo, Vector3f wi, TransportMode mode, bool keep, out SampledSpectrum sum, out Float density,
                          out bool polarised, out Attenuation matrix)
        {
            sum = new SampledSpectrum(0f);
            density = 0;
            polarised = false;
            matrix = default;
            bool flipped = false;
            Float side = 1;
            if (wo.z < 0)
            {
                wo = -wo;
                wi = -wi;
                flipped = true;
                side = -1;
            }
            bool fromCamera = mode == TransportMode.Radiance;
            RNG rng = new RNG(Hash(wo), Hash(wi));
            SampledSpectrum energy = new SampledSpectrum(1f);
            Attenuation total = default, chain = default;
            bool hasTotal = false, hasChain = false;
            Vector3f wr = -wo;
            Float h = MicrosurfaceWalk.StartHeight(height);
            bool outside = true;
            for (int order = 0; order < maxOrder; ++order)
            {
                h = NextHeight(wr, h, outside, rng.Uniform<Float>());
                if (h == MicrosurfaceWalk.Left || h == -MicrosurfaceWalk.Left)
                {
                    break;
                }
                Vector3f incoming = -wr;
                Float sign = outside ? 1 : -1;

                // What this facet would reflect to wi, and what lets it out along wi
                Vector3f wh = incoming + wi;
                if (LengthSquared(wh) > 0)
                {
                    wh = Normalize(wh);
                    Float cosineTo = Dot(incoming, wh);
                    Float geometry = cosineTo > 0 ? 0.25f * mfDistrib.PDF_SingleSided(sign * incoming, sign * wh) / cosineTo : 0;
                    if (geometry > 0 && !IsNaN(geometry) && !IsInf(geometry))
                    {
                        SampledSpectrum R, T;
                        Float reflectance = Mirror(cosineTo, outside != flipped, energy, out R, out T);
                        Attenuation path = chain;
                        bool hasPath = hasChain;
                        bool valid = true;
                        if (keep)
                        {
                            Attenuation facet;
                            valid = Facet(bsdf, side, incoming, wi, wh, cosineTo, outside != flipped, true, fromCamera, out facet);
                            if (valid)
                            {
                                path = PolarisedFacet.Compose(chain, hasChain, facet, fromCamera);
                                hasPath = true;
                            }
                        }
                        if (valid)
                        {
                            SampledSpectrum through;
                            Float throughProbability;
                            Escape(bsdf, side, wi, h, outside, flipped, fromCamera, keep, ref rng, energy * R, out through, out throughProbability, ref path, ref hasPath);
                            SampledSpectrum contributed = energy * R * through * geometry;
                            sum += contributed;
                            density += reflectance * throughProbability * geometry;
                            if (keep && (bool)contributed)
                            {
                                Attenuation contribution = path * contributed;
                                if (hasTotal)
                                {
                                    total += contribution;
                                }
                                else
                                {
                                    total = contribution;
                                    hasTotal = true;
                                }
                            }
                        }
                    }
                }

                // The walk goes on: this facet reflects it or lets it through
                Vector3f wm = SampleNormal(incoming, outside, ref rng);
                Float cosine = Dot(incoming, wm);
                SampledSpectrum reflectanceAt, transmittanceAt;
                // (by what the walk carries: see Mirror())
                Float probability = Mirror(cosine, outside != flipped, energy, out reflectanceAt, out transmittanceAt);
                bool reflected = rng.Uniform<Float>() < probability;
                bool wasOutside = outside;
                Vector3f next;
                if (reflected)
                {
                    energy *= reflectanceAt / probability;
                    next = -incoming + 2 * cosine * wm;
                }
                else
                {
                    energy *= transmittanceAt / (1 - probability);
                    next = wr;
                    outside = !outside;
                }
                if (IsNaN(next.x + next.y + next.z) || IsNaN(h) || !(bool)energy)
                {
                    break;
                }
                if (keep)
                {
                    Attenuation step;
                    if (!Facet(bsdf, side, incoming, next, wm, cosine, wasOutside != flipped, reflected, fromCamera, out step))
                    {
                        break;
                    }
                    chain = PolarisedFacet.Compose(chain, hasChain, step, fromCamera);
                    hasChain = true;
                }
                wr = next;
            }
            if (keep && hasTotal)
            {
                polarised = PolarisedFacet.Normalise(total, sum, new SampledSpectrum(1f), out matrix);
            }
        }

        // Asked without polarisation, the walk still keeps the polarisation of the light between its events and returns what
        // unpolarised light gives: the value times the first element of its matrix. (Without it every event reflects the mean
        // of the two polarisations, as the integrators without polarisation do between surfaces.)
        public bool polarisedInside;

        private SampledSpectrum Inside(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            SampledSpectrum value = f(new BSDF(new Normal3f(0, 0, 1), new Vector3f(1, 0, 0), this), wo, wi, mode, out bool polarised, out Attenuation matrix);
            return polarised ? value * matrix.GetMuellerMatrix().Get(0, 0) : value;
        }

        private pstd.optional<BSDFSample> Inside(Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            pstd.optional<BSDFSample> sampled = Sample_f(new BSDF(new Normal3f(0, 0, 1), new Vector3f(1, 0, 0), this), wo, uc, u, mode, sampleFlags,
                                                         out bool polarised, out Attenuation matrix);
            if (!sampled || !polarised)
            {
                return sampled;
            }
            BSDFSample bs = sampled.value();
            bs.f *= matrix.GetMuellerMatrix().Get(0, 0);
            return bs;
        }

        public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            if (wo.z == 0 || wi.z == 0 || mfDistrib.EffectivelySmooth())
            {
                return new SampledSpectrum(0f);
            }
            if (polarisedInside && !carriedPolarised)
            {
                return Inside(wo, wi, mode);
            }
            if (single)
            {
                return SingleValue(wo, wi);
            }
            SampledSpectrum sum;
            Walk(default, wo, wi, mode, false, out sum, out Float _, out bool _, out Attenuation _);
            return sum / AbsCosTheta(wi);
        }

        public override SampledSpectrum f(BSDF bsdf, Vector3f wo, Vector3f wi, TransportMode mode, out bool polarised, out Attenuation matrix)
        {
            polarised = false;
            matrix = default;
            if (wo.z == 0 || wi.z == 0 || mfDistrib.EffectivelySmooth())
            {
                return new SampledSpectrum(0f);
            }
            if (single)
            {
                // The one facet of the paper's model: its matrix
                SampledSpectrum value = SingleValue(wo, wi);
                if ((bool)value)
                {
                    bool below = wo.z < 0;
                    Vector3f a = below ? -wo : wo, b = below ? -wi : wi;
                    Vector3f wm = Normalize(a + b);
                    polarised = Facet(bsdf, below ? -1 : 1, a, b, wm, Dot(a, wm), !below, true, mode == TransportMode.Radiance, out matrix);
                }
                return value;
            }
            SampledSpectrum sum;
            Walk(bsdf, wo, wi, mode, true, out sum, out Float _, out polarised, out matrix);
            return sum / AbsCosTheta(wi);
        }

        public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if (wo.z == 0 || wi.z == 0 || mfDistrib.EffectivelySmooth())
            {
                return 0;
            }
            BxDFReflTransFlags wanted = SameHemisphere(wo, wi) ? BxDFReflTransFlags.Reflection : BxDFReflTransFlags.Transmission;
            if ((sampleFlags & wanted) == 0)
            {
                return 0;
            }
            if (single)
            {
                return SingleDensity(wo, wi, sampleFlags);
            }
            Float density;
            Walk(default, wo, wi, mode, false, out SampledSpectrum _, out density, out bool _, out Attenuation _);
            return std.max<Float>(density, 1e-6f);
        }

        public override pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if (polarisedInside && !carriedPolarised)
            {
                return Inside(wo, uc, u, mode, sampleFlags);
            }
            // (inside a stack that is evaluated with polarisation: the walk that its matrix will be asked of)
            return Sample(carriedFrame, wo, uc, u, mode, sampleFlags, carriedPolarised, out bool _, out Attenuation _);
        }

        public override pstd.optional<BSDFSample> Sample_f(BSDF bsdf, Vector3f wo, Float uc, Point2f u, TransportMode mode,
                                                           BxDFReflTransFlags sampleFlags, out bool polarised, out Attenuation matrix)
        {
            return Sample(bsdf, wo, uc, u, mode, sampleFlags, true, out polarised, out matrix);
        }

        private pstd.optional<BSDFSample> Sample(BSDF bsdf, Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags,
                                                 bool keep, out bool polarised, out Attenuation matrix)
        {
            polarised = false;
            matrix = default;
            if (wo.z == 0)
            {
                return default;
            }
            bool fromCamera = mode == TransportMode.Radiance;
            bool flipped = wo.z < 0;
            Float side = flipped ? -1 : 1;
            Vector3f start = flipped ? -wo : wo;
            SampledSpectrum carry = (bool)carried ? carried : new SampledSpectrum(1f);
            if (single || mfDistrib.EffectivelySmooth())
            {
                Float chosen = -1;
                if (keep && !single && carriedHasChain)
                {
                    Vector3f up = new Vector3f(0, 0, 1);
                    chosen = Polarised(bsdf, side, start, new Vector3f(-start.x, -start.y, start.z), up, start.z, !flipped, carry, carriedChain,
                                       fromCamera, out SampledSpectrum _, out SampledSpectrum _);
                }
                pstd.optional<BSDFSample> sampled = SingleSample(wo, uc, u, sampleFlags, chosen);
                if (keep && (bool)sampled)
                {
                    bool reflects = sampled.value().IsReflection();
                    if (mfDistrib.EffectivelySmooth())
                    {
                        // A flat mirror: the matrix of the films themselves
                        Vector3f leaving = reflects ? new Vector3f(-start.x, -start.y, start.z) : -start;
                        polarised = Facet(bsdf, side, start, leaving, new Vector3f(0, 0, 1), start.z, !flipped, reflects, fromCamera, out matrix);
                    }
                    else
                    {
                        // The one facet of the paper's model, which the sample drew (SingleSample())
                        Vector3f wm = mfDistrib.Sample_wm(wo, u);
                        if (flipped)
                        {
                            wm = -wm;
                        }
                        Vector3f leaving = reflects ? (flipped ? -sampled.value().wi : sampled.value().wi) : -start;
                        polarised = Facet(bsdf, side, start, leaving, wm, Dot(start, wm), !flipped, reflects, fromCamera, out matrix);
                    }
                }
                return sampled;
            }
            RNG rng = new RNG(Hash(wo), Hash(uc, u));
            Attenuation chain = default;
            bool hasChain = false;
            Vector3f wr = -start;
            Float h = MicrosurfaceWalk.StartHeight(height);
            bool outside = true;
            int reflections = 0;
            SampledSpectrum energy = new SampledSpectrum(1f);
            for (int order = 0; order <= maxOrder; ++order)
            {
                h = NextHeight(wr, h, outside, rng.Uniform<Float>());
                if (h == MicrosurfaceWalk.Left || h == -MicrosurfaceWalk.Left)
                {
                    if (order == 0 || (outside ? !(wr.z > 0) : !(wr.z < 0)))
                    {
                        return default;
                    }
                    BxDFReflTransFlags wanted = outside ? BxDFReflTransFlags.Reflection : BxDFReflTransFlags.Transmission;
                    if ((sampleFlags & wanted) == 0)
                    {
                        return default;
                    }
                    Vector3f wi = flipped ? -wr : wr;
                    polarised = keep && hasChain;
                    matrix = chain;
                    if (reflections == 0)
                    {
                        // Only through facets: the direction of the light itself
                        return new BSDFSample(energy / AbsCosTheta(wo), -wo, 1, BxDFFlags.SpecularTransmission, 1, true);
                    }
                    return new BSDFSample(energy / AbsCosTheta(wi), wi, 1, outside ? BxDFFlags.GlossyReflection : BxDFFlags.GlossyTransmission, 1, true);
                }
                if (order == maxOrder)
                {
                    break;
                }
                Vector3f incoming = -wr;
                Vector3f wm = SampleNormal(incoming, outside, ref rng);
                Float cosine = Dot(incoming, wm);
                SampledSpectrum R, T;
                Float probability;
                if (keep && (hasChain || carriedHasChain))
                {
                    // (with polarisation: by what the facet reflects of the light of this path)
                    Attenuation before = hasChain ? (carriedHasChain ? PolarisedFacet.Compose(carriedChain, true, chain, fromCamera) : chain) : carriedChain;
                    probability = Polarised(bsdf, side, incoming, -incoming + 2 * cosine * wm, wm, cosine, outside != flipped, carry * energy, before,
                                            fromCamera, out R, out T);
                }
                else
                {
                    probability = Mirror(cosine, outside != flipped, carry * energy, out R, out T);
                }
                bool reflected = rng.Uniform<Float>() < probability;
                bool wasOutside = outside;
                Vector3f next;
                if (reflected)
                {
                    energy *= R / probability;
                    next = -incoming + 2 * cosine * wm;
                    ++reflections;
                }
                else
                {
                    energy *= T / (1 - probability);
                    next = wr;
                    outside = !outside;
                }
                if (IsNaN(next.x + next.y + next.z) || IsNaN(h) || !(bool)energy)
                {
                    return default;
                }
                if (keep)
                {
                    Attenuation step;
                    if (!Facet(bsdf, side, incoming, next, wm, cosine, wasOutside != flipped, reflected, fromCamera, out step))
                    {
                        return default;
                    }
                    chain = PolarisedFacet.Compose(chain, hasChain, step, fromCamera);
                    hasChain = true;
                }
                wr = next;
            }
            return default;
        }

        // The model of the paper: one facet
        private SampledSpectrum SingleValue(Vector3f wo, Vector3f wi)
        {
            if (!SameHemisphere(wo, wi))
            {
                return new SampledSpectrum(0f);
            }
            Vector3f wm = wi + wo;
            if (LengthSquared(wm) == 0)
            {
                return new SampledSpectrum(0f);
            }
            wm = Normalize(wm);
            SampledSpectrum R, T;
            Mirror(AbsDot(wo, wm), wo.z > 0, out R, out T);
            return R * (mfDistrib.DG(wo, wi, wm) / (4 * AbsCosTheta(wi) * AbsCosTheta(wo)));
        }

        private Float SingleDensity(Vector3f wo, Vector3f wi, BxDFReflTransFlags sampleFlags)
        {
            if (!SameHemisphere(wo, wi))
            {
                return 0;
            }
            Vector3f wm = wi + wo;
            if (LengthSquared(wm) == 0)
            {
                return 0;
            }
            wm = FaceForward(Normalize(wm), new Normal3f(0, 0, 1));
            SampledSpectrum R, T;
            Mirror(AbsDot(wo, wm), wo.z > 0, out R, out T);
            Float pr = R.Average();
            Float pt = (sampleFlags & BxDFReflTransFlags.Transmission) == 0 ? 0 : T.Average();
            if (pr == 0)
            {
                return 0;
            }
            return mfDistrib.PDF(wo, wm) / (4 * AbsDot(wo, wm)) * pr / (pr + pt);
        }

        // `chosen`: the probability of the reflection, when the caller has one (negative: by the reflectance)
        private pstd.optional<BSDFSample> SingleSample(Vector3f wo, Float uc, Point2f u, BxDFReflTransFlags sampleFlags, Float chosen)
        {
            bool smooth = mfDistrib.EffectivelySmooth();
            Vector3f wm = smooth ? new Vector3f(0, 0, 1) : mfDistrib.Sample_wm(wo, u);
            SampledSpectrum R, T;
            Mirror(AbsDot(wo, wm), wo.z > 0, out R, out T);
            // Reflection or transmission by the reflectance of the facet, averaged over the wavelengths: the path keeps them all
            // (a smooth layer by what the path carries; the one facet model has a density that is used, and keeps its own)
            SampledSpectrum carry = smooth && (bool)carried ? carried : new SampledSpectrum(1f);
            Float pr = (carry * R).Average(), pt = (carry * T).Average();
            if (!(pr + pt > 0) || IsInf(pr + pt))
            {
                pr = R.Average();
                pt = T.Average();
            }
            if (chosen >= 0 && pr + pt > 0)
            {
                // (kept away from 0 and 1: a part that the mean reflectance has is still sampled)
                pr = Clamp(chosen, pr > 0 ? 0.02f : 0, pt > 0 ? 0.98f : 1);
                pt = 1 - pr;
            }
            if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0)
            {
                pr = 0;
            }
            if ((sampleFlags & BxDFReflTransFlags.Transmission) == 0)
            {
                pt = 0;
            }
            if (pr == 0 && pt == 0)
            {
                return default;
            }
            if (uc < pr / (pr + pt))
            {
                if (smooth)
                {
                    Vector3f mirrored = new Vector3f(-wo.x, -wo.y, wo.z);
                    return new BSDFSample(R / AbsCosTheta(mirrored), mirrored, pr / (pr + pt), BxDFFlags.SpecularReflection);
                }
                Vector3f wi = Reflect(wo, wm);
                if (!SameHemisphere(wo, wi))
                {
                    return default;
                }
                Float pdf = mfDistrib.PDF(wo, wm) / (4 * AbsDot(wo, wm)) * pr / (pr + pt);
                SampledSpectrum value = R * (mfDistrib.DG(wo, wi, wm) / (4 * AbsCosTheta(wi) * AbsCosTheta(wo)));
                return new BSDFSample(value, wi, pdf, BxDFFlags.GlossyReflection);
            }
            // Straight through: the transmittance of this facet, one of those that the direction sees, and the shadowing of the
            // direction on its way out (the facets were drawn with the masking of the way in)
            Float shadowing = smooth ? 1 : mfDistrib.G1(wo);
            return new BSDFSample(T * (shadowing / AbsCosTheta(wo)), -wo, pt / (pr + pt), BxDFFlags.SpecularTransmission);
        }

        public override void Regularize() { mfDistrib.Regularize(); }

        public override string ToString()
        {
            return StringPrintf("[ BraggLayerBxDF hostEta: %f cells: %d mfDistrib: %s single: %s ]", hostEta, film.repeat, mfDistrib.ToString(), single);
        }

        // What the path carries at each wavelength when the layer is sampled inside a stack (LayeredBxDF.carried): zero is one
        public SampledSpectrum carried;
        // With polarisation (LayeredBxDF.carriedPolarised): the frame of the surface and the matrix of the path that led here
        public bool carriedPolarised;
        public BSDF carriedFrame;
        public Attenuation carriedChain;
        public bool carriedHasChain;

        private ThinFilmStack film;
        private Float hostEta;
        private MicrofacetDistribution mfDistrib;
        private MicrosurfaceHeight height;
        private bool single;
        private int maxOrder;
    }

    // BraggMaterial Definition
    public sealed unsafe class BraggMaterial : Material
    {
        public BraggMaterial(ThinFilmLayers layers, Float hostEta, SpectrumTexture reflectance, bool hasBase, FloatTexture uRoughness, FloatTexture vRoughness,
                             bool remapRoughness, MicrofacetDistributionType distribution, Float gamma, int maxDepth, int nSamples,
                             FloatTexture displacement, Image normalMap, bool single, int scatterDepth, int[] mirrorFilms, int[] mirrorRepeat)
        {
            this.mirrorFilms = mirrorFilms;
            this.mirrorRepeat = mirrorRepeat;
            this.single = single;
            this.scatterDepth = scatterDepth;
            this.layers = layers;
            this.hostEta = hostEta;
            this.reflectance = reflectance;
            this.hasBase = hasBase;
            this.uRoughness = uRoughness;
            this.vRoughness = vRoughness;
            this.remapRoughness = remapRoughness;
            this.distribution = distribution;
            this.gamma = gamma;
            this.maxDepth = maxDepth;
            this.nSamples = nSamples;
            this.displacement = displacement;
            this.normalMap = normalMap;
        }

        public override bool CanEvaluateTextures<TextureEvaluator>(TextureEvaluator texEval)
        {
            return texEval.CanEvaluate(new FloatTexture[] { uRoughness, vRoughness }, new SpectrumTexture[] { reflectance });
        }

        public override BSDF GetBSDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                       ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            Float uRough = texEval.Apply(uRoughness, ctx), vRough = texEval.Apply(vRoughness, ctx);
            if (remapRoughness)
            {
                uRough = TrowbridgeReitzDistribution.RoughnessToAlpha(uRough);
                vRough = TrowbridgeReitzDistribution.RoughnessToAlpha(vRough);
            }
            MicrofacetDistribution distrib = MicrofacetDistribution.Create(distribution, uRough, vRough, gamma);
            // (the films cost no wavelength: the light goes straight through the layer, whatever their indices)
            ThinFilmStack films = layers.Stack(texEval, ctx, lambda);
            // The surface of the host, the mirrors and the base side by side in one stack, with one walk through all of them
            // (LayerStackBxDF). One layered BxDF of pbrt inside another for each layer does not do: with a mirror in the inner
            // one its value is not what its samples carry (bragg_test.cs, Stacks).
            List<BxDF> interfaces = new List<BxDF>();
            if (hasBase && hostEta != 1)
            {
                interfaces.Add(new DielectricBxDF(hostEta, new TrowbridgeReitzDistribution(0, 0)));
            }
            if (mirrorFilms == null)
            {
                interfaces.Add(new BraggLayerBxDF(films, hostEta, distrib, single, scatterDepth));
            }
            else
            {
                // Several mirrors, the first on top: each is a layer of its own in the host
                int start = 0;
                for (int mirror = 0; mirror < mirrorFilms.Length; ++mirror)
                {
                    interfaces.Add(new BraggLayerBxDF(films.Part(start, mirrorFilms[mirror], mirrorRepeat[mirror]), hostEta, distrib, single, scatterDepth));
                    start += mirrorFilms[mirror];
                }
            }
            if (hasBase)
            {
                interfaces.Add(new DiffuseBxDF(Clamp(texEval.Apply(reflectance, ctx, lambda), 0, 1)));
            }
            // (the host between them is clear)
            LayerMedium[] media = new LayerMedium[interfaces.Count - 1];
            for (int i = 0; i < media.Length; ++i)
            {
                media[i].thickness = 1;
                media[i].albedo = new SampledSpectrum(0f);
                media[i].sigmaA = new SampledSpectrum(0f);
            }
            if (interfaces.Count == 1)
            {
                BraggLayerBxDF alone = (BraggLayerBxDF)interfaces[0];
                alone.polarisedInside = polarisedInside;
                return new BSDF(ctx.n, ctx.ns, ctx.dpdus, buf.Alloc(alone));
            }
            LayerStackBxDF stack = new LayerStackBxDF(interfaces.ToArray(), media, maxDepth, nSamples, hasBase);
            stack.polarisedInside = polarisedInside;
            return new BSDF(ctx.n, ctx.ns, ctx.dpdus, buf.Alloc(stack));
        }

        public static string Name() { return "BraggMaterial"; }

        public override FloatTexture GetDisplacement() { return displacement; }
        public override Image GetNormalMap() { return normalMap; }
        public override bool HasSubsurfaceScattering() { return false; }

        public override BSSRDF GetBSSRDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                           ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            return null;
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ BraggMaterial layers: %d cells: %d hostEta: %f ]", layers.Count(), layers.repeat, hostEta);
        }

        public static BraggMaterial Create(TextureParameterDictionary parameters, Image normalMap, FileLoc loc, Allocator alloc)
        {
            ThinFilmLayers layers = ThinFilmLayers.Create(parameters, loc, alloc);
            if (layers.Count() == 0)
            {
                pbrt.ErrorExit(loc, "\"bragg\" needs the films of a cell: \"float layereta\" and \"float layerthickness\" (and \"integer layerrepeat\").");
            }
            // Several mirrors: the number of films of the cell of each, and its number of cells
            int[] mirrorFilms = null, mirrorRepeat = null;
            List<int> filmCounts = parameters.GetIntArray("mirrorfilms");
            if (filmCounts.Count != 0)
            {
                List<int> cellCounts = parameters.GetIntArray("mirrorrepeat");
                int total = 0;
                foreach (int count in filmCounts)
                {
                    if (count < 1)
                    {
                        pbrt.ErrorExit(loc, "\"mirrorfilms\": a mirror has one film or more.");
                    }
                    total += count;
                }
                if (total != layers.Count() || cellCounts.Count != filmCounts.Count)
                {
                    pbrt.ErrorExit(loc, "\"mirrorfilms\" must add up to the number of films, and \"mirrorrepeat\" needs a value for each mirror.");
                }
                foreach (int count in cellCounts)
                {
                    if (count < 1)
                    {
                        pbrt.ErrorExit(loc, "\"mirrorrepeat\" must be 1 or more.");
                    }
                }
                mirrorFilms = filmCounts.ToArray();
                mirrorRepeat = cellCounts.ToArray();
            }
            Float hostEta = parameters.GetOneFloat("hosteta", 1f);
            string baseName = parameters.GetOneString("base", "diffuse");
            if (baseName != "diffuse" && baseName != "none")
            {
                pbrt.ErrorExit(loc, pbrt.StringPrintf("%s: base unknown (diffuse, none).", baseName));
            }
            if (!(hostEta >= 1) || (baseName == "none" && hostEta != 1))
            {
                pbrt.ErrorExit(loc, "\"bragg\": \"hosteta\" must be 1 or more, and 1 without a base (a layer alone is in the air).");
            }
            string scattering = parameters.GetOneString("scattering", "multiple");
            if (scattering != "multiple" && scattering != "single")
            {
                pbrt.ErrorExit(loc, pbrt.StringPrintf("%s: scattering unknown (multiple, single).", scattering));
            }
            SpectrumTexture reflectance = parameters.GetSpectrumTexture("reflectance", null, SpectrumType.Albedo, alloc);
            if (reflectance == null)
            {
                reflectance = new SpectrumConstantTexture(new ConstantSpectrum(0.05f));
            }
            FloatTexture uRoughness = parameters.GetFloatTextureOrNull("uroughness", alloc);
            FloatTexture vRoughness = parameters.GetFloatTextureOrNull("vroughness", alloc);
            if (uRoughness == null)
            {
                uRoughness = parameters.GetFloatTexture("roughness", 0f, alloc);
            }
            if (vRoughness == null)
            {
                vRoughness = parameters.GetFloatTexture("roughness", 0f, alloc);
            }
            BraggMaterial material = new BraggMaterial(layers, hostEta, reflectance, baseName == "diffuse", uRoughness, vRoughness, parameters.GetOneBool("remaproughness", true),
                                     MicrofacetDistribution.ParseType(parameters.GetOneString("distribution", "ggx"), loc), parameters.GetOneFloat("gamma", 2f),
                                     parameters.GetOneInt("maxdepth", 1000000), parameters.GetOneInt("nsamples", 1),
                                     parameters.GetFloatTextureOrNull("displacement", alloc), normalMap, scattering == "single",
                                     parameters.GetOneInt("scatterdepth", 1000000), mirrorFilms, mirrorRepeat);
            material.polarisedInside = parameters.GetOneBool("polarisedinside", false);
            return material;
        }

        private bool polarisedInside;
        private ThinFilmLayers layers;
        private Float hostEta;
        private SpectrumTexture reflectance;
        private bool hasBase;
        private FloatTexture uRoughness, vRoughness;
        private bool remapRoughness;
        private MicrofacetDistributionType distribution;
        private Float gamma;
        private int maxDepth, nSamples, scatterDepth;
        private int[] mirrorFilms, mirrorRepeat;
        private bool single;
        private FloatTexture displacement;
        private Image normalMap;
    }
}
