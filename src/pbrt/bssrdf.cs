// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// bssrdf.h, bssrdf.cpp

using System;

namespace pbrt
{

    // BSSRDFSample Definition
    public struct BSSRDFSample
    {
        public SampledSpectrum Sp, pdf;
        public BSDF Sw;
        public Vector3f wo;
    }

    // SubsurfaceInteraction Definition
    public unsafe class SubsurfaceInteraction
    {
        // SubsurfaceInteraction Public Methods
        public SubsurfaceInteraction() { }
        public SubsurfaceInteraction(SurfaceInteraction si)
        {
            pi = si.pi;
            n = si.n;
            dpdu = si.dpdu;
            dpdv = si.dpdv;
            ns = si.shading.n;
            dpdus = si.shading.dpdu;
            dpdvs = si.shading.dpdv;
        }

        public static implicit operator SurfaceInteraction(SubsurfaceInteraction s)
        {
            SurfaceInteraction si = new SurfaceInteraction();
            si.pi = s.pi;
            si.n = s.n;
            si.dpdu = s.dpdu;
            si.dpdv = s.dpdv;
            si.shading.n = s.ns;
            si.shading.dpdu = s.dpdus;
            si.shading.dpdv = s.dpdvs;
            return si;
        }

        public Point3f p() { return (Point3f)pi; }

        // SubsurfaceInteraction Public Members
        public Point3fi pi;
        public Normal3f n, ns;
        public Vector3f dpdu, dpdv, dpdus, dpdvs;
    }

    // BSSRDFTable Definition
    public unsafe class BSSRDFTable
    {
        // BSSRDFTable Public Members
        public pstd.vector<Float> rhoSamples, radiusSamples;
        public pstd.vector<Float> profile;
        public pstd.vector<Float> rhoEff;
        public pstd.vector<Float> profileCDF;

        // BSSRDFTable Public Methods
        public BSSRDFTable(int nRhoSamples, int nRadiusSamples, Allocator alloc)
        {
            rhoSamples = new pstd.vector<Float>((ulong)nRhoSamples);
            radiusSamples = new pstd.vector<Float>((ulong)nRadiusSamples);
            profile = new pstd.vector<Float>((ulong)(nRadiusSamples * nRhoSamples));
            rhoEff = new pstd.vector<Float>((ulong)nRhoSamples);
            profileCDF = new pstd.vector<Float>((ulong)(nRadiusSamples * nRhoSamples));
        }

        public override string ToString()
        {
            return Globals.StringPrintf("[ BSSRDFTable rhoSamples: %s radiusSamples: %s profile: %s " +
                                        "rhoEff: %s profileCDF: %s ]", rhoSamples, radiusSamples, profile, rhoEff, profileCDF);
        }

        public Float EvalProfile(int rhoIndex, int radiusIndex)
        {
            Globals.CHECK(rhoIndex >= 0 && rhoIndex < (int)rhoSamples.size());
            Globals.CHECK(radiusIndex >= 0 && radiusIndex < (int)radiusSamples.size());
            return profile[rhoIndex * (int)radiusSamples.size() + radiusIndex];
        }
    }

    // BSSRDFProbeSegment Definition
    public struct BSSRDFProbeSegment
    {
        // BSSRDFProbeSegment Public Methods
        public BSSRDFProbeSegment(Point3f p0, Point3f p1) { this.p0 = p0; this.p1 = p1; }
        public Point3f p0, p1;
    }

    // TabulatedBSSRDF Definition
    public sealed unsafe class TabulatedBSSRDF : BSSRDF
    {
        // TabulatedBSSRDF Public Methods
        public TabulatedBSSRDF() { }
        public TabulatedBSSRDF(Point3f po, Normal3f ns, Vector3f wo, Float eta, SampledSpectrum sigma_a, SampledSpectrum sigma_s,
                               BSSRDFTable table)
        {
            this.po = po; this.wo = wo; this.eta = eta; this.ns = ns; this.table = table;
            sigma_t = sigma_a + sigma_s;
            rho = Globals.SafeDiv(sigma_s, sigma_t);
        }

        public SampledSpectrum Sp(Point3f pi) { return Sr(Globals.Distance(po, pi)); }

        public SampledSpectrum Sr(Float r)
        {
            SampledSpectrum Sr = new SampledSpectrum(0f);
            Float[] rhoWeights = new Float[4];
            Float[] radiusWeights = new Float[4];
            for (int i = 0; i < Globals.NSpectrumSamples; ++i)
            {
                // Convert $r$ into unitless optical radius $r_{\roman{optical}}$
                Float rOptical = r * sigma_t[i];

                // Compute spline weights to interpolate BSSRDF at _i_th wavelength
                int rhoOffset, radiusOffset;
                if (!Globals.CatmullRomWeights(Globals.pstdspan(table.rhoSamples), rho[i], &rhoOffset, new pstd.span<Float>(rhoWeights)) ||
                    !Globals.CatmullRomWeights(Globals.pstdspan(table.radiusSamples), rOptical, &radiusOffset,
                                               new pstd.span<Float>(radiusWeights)))
                    continue;

                // Set BSSRDF value _Sr[i]_ using tensor spline interpolation
                Float sr = 0;
                for (int j = 0; j < 4; ++j)
                    for (int k = 0; k < 4; ++k)
                    {
                        // Accumulate contribution of $(j,k)$ table sample
                        Float weight = rhoWeights[j] * radiusWeights[k];
                        if (weight != 0)
                            sr += weight * table.EvalProfile(rhoOffset + j, radiusOffset + k);
                    }

                // Cancel marginal PDF factor from tabulated BSSRDF profile
                if (rOptical != 0)
                    sr /= 2 * Globals.Pi * rOptical;
                Sr[i] = sr;
            }
            // Transform BSSRDF value into rendering space units
            Sr *= Globals.Sqr(sigma_t);
            return Globals.ClampZero(Sr);
        }

        public pstd.optional<Float> SampleSr(Float u)
        {
            if (sigma_t[0] == 0)
                return default;
            return Globals.SampleCatmullRom2D(Globals.pstdspan(table.rhoSamples), Globals.pstdspan(table.radiusSamples),
                                              Globals.pstdspan(table.profile), Globals.pstdspan(table.profileCDF), rho[0], u) /
                   sigma_t[0];
        }

        public SampledSpectrum PDF_Sr(Float r)
        {
            SampledSpectrum pdf = new SampledSpectrum(0f);
            Float[] rhoWeights = new Float[4];
            Float[] radiusWeights = new Float[4];
            for (int i = 0; i < Globals.NSpectrumSamples; ++i)
            {
                // Convert $r$ into unitless optical radius $r_{\roman{optical}}$
                Float rOptical = r * sigma_t[i];

                // Compute spline weights to interpolate BSSRDF at _i_th wavelength
                int rhoOffset, radiusOffset;
                if (!Globals.CatmullRomWeights(Globals.pstdspan(table.rhoSamples), rho[i], &rhoOffset, new pstd.span<Float>(rhoWeights)) ||
                    !Globals.CatmullRomWeights(Globals.pstdspan(table.radiusSamples), rOptical, &radiusOffset,
                                               new pstd.span<Float>(radiusWeights)))
                    continue;

                // Set BSSRDF profile probability density for wavelength
                Float sr = 0, rhoEff = 0;
                for (int j = 0; j < 4; ++j)
                {
                    if (rhoWeights[j] == 0)
                        continue;
                    // Update _rhoEff_ and _sr_ for wavelength
                    rhoEff += table.rhoEff[rhoOffset + j] * rhoWeights[j];

                    for (int k = 0; k < 4; ++k)
                    {
                        if (radiusWeights[k] == 0)
                            continue;
                        sr += table.EvalProfile(rhoOffset + j, radiusOffset + k) * rhoWeights[j] * radiusWeights[k];
                    }
                }

                // Cancel marginal PDF factor from tabulated BSSRDF profile
                if (rOptical != 0)
                    sr /= 2 * Globals.Pi * rOptical;
                pdf[i] = sr * Globals.Sqr(sigma_t[i]) / rhoEff;
            }
            return Globals.ClampZero(pdf);
        }

        public override pstd.optional<BSSRDFProbeSegment> SampleSp(Float u1, Point2f u2)
        {
            // Choose projection axis for BSSRDF sampling
            Frame f;
            if (u1 < 0.25f)
                f = Frame.FromX(ns);
            else if (u1 < 0.5f)
                f = Frame.FromY(ns);
            else
                f = Frame.FromZ(ns);

            // Sample BSSRDF profile in polar coordinates
            pstd.optional<Float> r = SampleSr(u2[0]);
            if (!r)
                return default;
            Float phi = 2 * Globals.Pi * u2[1];

            // Compute BSSRDF profile bounds and intersection height
            pstd.optional<Float> r_max = SampleSr(0.999f);
            if (!r_max || r.value() >= r_max.value())
                return default;
            Float l = 2 * std.sqrt(Globals.Sqr(r_max.value()) - Globals.Sqr(r.value()));

            // Return BSSRDF sampling ray segment
            Point3f pStart = po + r.value() * (f.x * std.cos(phi) + f.y * std.sin(phi)) - l * f.z / 2;
            Point3f pTarget = pStart + l * f.z;
            return new BSSRDFProbeSegment(pStart, pTarget);
        }

        public SampledSpectrum PDF_Sp(Point3f pi, Normal3f ni)
        {
            // Express $\pti-\pto$ and $\N{}_\roman{i}$ with respect to local coordinates at
            // $\pto$
            Vector3f d = pi - po;
            Frame f = Frame.FromZ(ns);
            Vector3f dLocal = f.ToLocal(d);
            Normal3f nLocal = f.ToLocal(ni);

            // Compute BSSRDF profile radius under projection along each axis
            Float* rProj = stackalloc Float[3];
            rProj[0] = std.sqrt(Globals.Sqr(dLocal.y) + Globals.Sqr(dLocal.z));
            rProj[1] = std.sqrt(Globals.Sqr(dLocal.z) + Globals.Sqr(dLocal.x));
            rProj[2] = std.sqrt(Globals.Sqr(dLocal.x) + Globals.Sqr(dLocal.y));

            // Return combined probability from all BSSRDF sampling strategies
            SampledSpectrum pdf = new SampledSpectrum(0f);
            Float* axisProb = stackalloc Float[3];
            axisProb[0] = .25f; axisProb[1] = .25f; axisProb[2] = .5f;
            for (int axis = 0; axis < 3; ++axis)
                pdf += PDF_Sr(rProj[axis]) * std.abs(nLocal[axis]) * axisProb[axis];
            return pdf;
        }

        // C++ takes a scratch-allocated NormalizedFresnelBxDF* and assigns *bxdf = NormalizedFresnelBxDF(eta)
        public override BSSRDFSample ProbeIntersectionToSample(SubsurfaceInteraction si, ScratchBuffer scratchBuffer)
        {
            NormalizedFresnelBxDF bxdf = scratchBuffer.Alloc(new NormalizedFresnelBxDF(eta));
            Vector3f wo = (Vector3f)si.ns;
            BSDF bsdf = new BSDF(si.ns, si.dpdus, bxdf);
            return new BSSRDFSample { Sp = Sp(si.p()), pdf = PDF_Sp(si.p(), si.n), Sw = bsdf, wo = wo };
        }

        public override string ToString()
        {
            return Globals.StringPrintf("[ TabulatedBSSRDF po: %s eta: %f ns: %s sigma_t: %s rho: %s table: %s ]", po, eta, ns,
                                        sigma_t, rho, table);
        }

        // TabulatedBSSRDF Private Members
        private Point3f po;
        private Vector3f wo;
        private Normal3f ns;
        private Float eta;
        private SampledSpectrum sigma_t, rho;
        private BSSRDFTable table;
    }

    public static unsafe partial class Globals
    {
        internal static pstd.span<Float> pstdspan(pstd.vector<Float> v) { return new pstd.span<Float>(v.data(), 0, (int)v.size()); }

        // BSSRDF Inline Functions
        public static void SubsurfaceFromDiffuse(BSSRDFTable t, SampledSpectrum rhoEff, SampledSpectrum mfp,
                                                 SampledSpectrum* sigma_a, SampledSpectrum* sigma_s)
        {
            for (int c = 0; c < NSpectrumSamples; ++c)
            {
                Float rho = InvertCatmullRom(pstdspan(t.rhoSamples), pstdspan(t.rhoEff), rhoEff[c]);
                (*sigma_s)[c] = rho / mfp[c];
                (*sigma_a)[c] = (1 - rho) / mfp[c];
            }
        }

        // BSSRDF Function Definitions
        public static Float BeamDiffusionMS(Float sigma_s, Float sigma_a, Float g, Float eta, Float r)
        {
            const int nSamples = 100;
            Float Ed = 0;
            // Precompute information for dipole integrand
            // Compute reduced scattering coefficients $\sigmaps, \sigmapt$ and albedo $\rhop$
            Float sigmap_s = sigma_s * (1 - g);
            Float sigmap_t = sigma_a + sigmap_s;
            Float rhop = sigmap_s / sigmap_t;

            // Compute non-classical diffusion coefficient $D_\roman{G}$ using Equation
            // $(\ref{eq:diffusion-coefficient-grosjean})$
            Float D_g = (2 * sigma_a + sigmap_s) / (3 * sigmap_t * sigmap_t);

            // Compute effective transport coefficient $\sigmatr$ based on $D_\roman{G}$
            Float sigma_tr = SafeSqrt(sigma_a / D_g);

            // Determine linear extrapolation distance $\depthextrapolation$ using Equation
            // $(\ref{eq:dipole-boundary-condition})$
            Float fm1 = FresnelMoment1(eta), fm2 = FresnelMoment2(eta);
            Float ze = -2 * D_g * (1 + 3 * fm2) / (1 - 2 * fm1);

            // Determine exitance scale factors using Equations $(\ref{eq:kp-exitance-phi})$ and
            // $(\ref{eq:kp-exitance-e})$
            Float cPhi = 0.25f * (1 - 2 * fm1), cE = 0.5f * (1 - 3 * fm2);

            for (int i = 0; i < nSamples; ++i)
            {
                // Sample real point source depth $\depthreal$
                Float zr = SampleExponential((i + 0.5f) / nSamples, sigmap_t);

                // Evaluate dipole integrand $E_{\roman{d}}$ at $\depthreal$ and add to _Ed_
                Float zv = -zr + 2 * ze;
                Float dr = std.sqrt(Sqr(r) + Sqr(zr)), dv = std.sqrt(Sqr(r) + Sqr(zv));
                // Compute dipole fluence rate $\dipole(r)$ using Equation
                // $(\ref{eq:diffusion-dipole})$
                Float phiD = Inv4Pi / D_g * (FastExp(-sigma_tr * dr) / dr - FastExp(-sigma_tr * dv) / dv);

                // Compute dipole vector irradiance $-\N{}\cdot\dipoleE(r)$ using Equation
                // $(\ref{eq:diffusion-dipole-vector-irradiance-normal})$
                Float EDn = Inv4Pi * (zr * (1 + sigma_tr * dr) * FastExp(-sigma_tr * dr) / (Pow(3, dr)) -
                                      zv * (1 + sigma_tr * dv) * FastExp(-sigma_tr * dv) / (Pow(3, dv)));

                // Add contribution from dipole for depth $\depthreal$ to _Ed_
                Float E = phiD * cPhi + EDn * cE;
                Float kappa = 1 - FastExp(-2 * sigmap_t * (dr + zr));
                Ed += kappa * rhop * rhop * E;
            }
            return Ed / nSamples;
        }

        public static Float BeamDiffusionSS(Float sigma_s, Float sigma_a, Float g, Float eta, Float r)
        {
            // Compute material parameters and minimum $t$ below the critical angle
            Float sigma_t = sigma_a + sigma_s, rho = sigma_s / sigma_t;
            Float tCrit = r * SafeSqrt(Sqr(eta) - 1);

            Float Ess = 0;
            const int nSamples = 100;
            for (int i = 0; i < nSamples; ++i)
            {
                // Evaluate single-scattering integrand and add to _Ess_
                Float ti = tCrit + SampleExponential((i + 0.5f) / nSamples, sigma_t);
                // Determine length $d$ of connecting segment and $\cos\theta_\roman{o}$
                Float d = std.sqrt(Sqr(r) + Sqr(ti));
                Float cosTheta_o = ti / d;

                // Add contribution of single scattering at depth $t$
                Ess += rho * FastExp(-sigma_t * (d + tCrit)) / Sqr(d) * HenyeyGreenstein(cosTheta_o, g) *
                       (1 - FrDielectric(-cosTheta_o, eta)) * std.abs(cosTheta_o);
            }
            return Ess / nSamples;
        }

        public static void ComputeBeamDiffusionBSSRDF(Float g, Float eta, BSSRDFTable t)
        {
            // Choose radius values of the diffusion profile discretization
            t.radiusSamples[0] = 0;
            t.radiusSamples[1] = 2.5e-3f;
            for (int i = 2; i < (int)t.radiusSamples.size(); ++i)
                t.radiusSamples[i] = t.radiusSamples[i - 1] * 1.2f;

            // Choose albedo values of the diffusion profile discretization
            for (int i = 0; i < (int)t.rhoSamples.size(); ++i)
                t.rhoSamples[i] = (1 - FastExp(-8 * i / (Float)(t.rhoSamples.size() - 1))) / (1 - FastExp(-8f));

            ParallelFor(0, (long)t.rhoSamples.size(), (int i) =>
            {
                // Compute the diffusion profile for the _i_th albedo sample
                // Compute scattering profile for chosen albedo $\rho$
                int nSamples = (int)t.radiusSamples.size();
                for (int j = 0; j < nSamples; ++j)
                {
                    Float rho = t.rhoSamples[i], r = t.radiusSamples[j];
                    t.profile[i * nSamples + j] = 2 * Pi * r * (BeamDiffusionSS(rho, 1 - rho, g, eta, r) +
                                                                BeamDiffusionMS(rho, 1 - rho, g, eta, r));
                }

                // Compute effective albedo $\rho_{\roman{eff}}$ and CDF for importance sampling
                t.rhoEff[i] = IntegrateCatmullRom(pstdspan(t.radiusSamples),
                                                  new pstd.span<Float>(t.profile.data(), i * nSamples, nSamples),
                                                  new pstd.span<Float>(t.profileCDF.data(), i * nSamples, nSamples));
            });
        }
    }

}  // namespace pbrt
