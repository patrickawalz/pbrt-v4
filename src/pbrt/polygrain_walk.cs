// (not in pbrt-v4, not in the fork) polygrain_walk.cs
//
// Multiple scattering for the polydisperse micrograin BSDF ("scatterdepth" above 1): the position-free random walk of
//   F. Zhou, H. Shen, M. Li, Y. Zhao, C. Bi, "Position-free multiple-scattering computations for micrograin BSDF model",
//   Graphical Models 141, 2025 [ZSL*25]
// adapted to the model of XLRBB26. Their walk is kept: a ray meets the grains or the base, a grain reflects it about a visible
// normal found by rejection sampling, and after each scattering the ray leaves the surface or meets a grain or the base again,
// with no position kept. Their probabilities are replaced, each by what the polydisperse model itself gives:
//
//   ZSL*25 (one type of grain, Smith masking)         here
//   entering: grains with tau+(theta)                 grains with 1 - Vp(w), Vp the pore visibility (XLRBB26 Eq. 15). Exact:
//                                                     the grains seen from w cover cos(theta) (1 - Vp(w)) (polygrain_test.cs)
//   visible normals by rejection from a GGX VNDF      the type and the normal by rejection from a proposal uniform in the
//   with a tabulated bound                            height of each slice of each type, which bounds the target in closed form
//   leaving a grain upwards: G1(w), Smith             the distant GAF of the point, prod_j Gdist_j(w, h / r_j), at the height h
//                                                     of the point; after the first scattering the GAF of the two directions
//                                                     over that of the first, Gdist~(wo, w) / Gdist~(wo), so that the first
//                                                     order of the walk is the BSDF of the paper, exactly
//   from a grain downwards: the base with 1 - tau0    the base with prod_j Gdist_j(-w, 0) / Gdist_j(-w, h / r_j): the ray seen
//                                                     backwards from the base, free up to the height h (an approximation of
//                                                     ours: the grains that would block the ray both below and above h are
//                                                     counted once)
//   leaving the base: Delta, a parameter              the pore visibility again, prod_j Gdist_j(w, 0) (after the first
//                                                     scattering, that of the two directions over that of the first)
// A ray that is stopped on its way up from the height h meets a grain above h, one stopped on its way down a grain below h,
// with a normal that faces it: the normals are those of the grains weighted by the projected area along the ray, without a
// masking term (the position-free step), within those heights.
//
// Sample_f follows one walk; its weight is the product of the reflectances and its density is not known (pdfIsProportional).
// f is the BSDF of the paper plus the later scatterings, each connected to wi at every event of the walk (next event
// estimation): at a specular grain through the density of the normal at the half vector, whose normalisation over the grains
// that the ray can meet has no closed form and is estimated without bias by the number of trials of the rejection sampling
// (a geometric variable: its mean is the proposal mass over that normalisation). PDF is the same estimate with unit weights.
//
// The first order is reciprocal (it is the paper's). The later orders are built from one-sided probabilities and one walk is
// not reciprocal (by 2 % of f, polygrain_walk_test.cs): with `reciprocal` (the default) f is the mean of the walks from the two
// directions, which is. Sample_f then follows the walk from wo for the half that it samples and, for the other half, takes a
// share of its samples by the cosine and evaluates the walk from there: its mean is f.

using System;
using Attenuation = pbrt.PolarisedLightSystem.Attenuation;

namespace pbrt
{
    public sealed unsafe partial class PolyGrainBxDF
    {
        private enum Heights
        {
            Entry,  // a ray from outside: every height, with the distant GAF of the direction
            Above,  // a ray stopped on its way up: the grains above its height
            Below   // a ray stopped on its way down: the grains below its height
        }

        // The proposal of the rejection sampling: for each slice of heights k and each type i present in it, the height uniform
        // in the slice and the azimuth uniform. In those variables the grains have the measure a_i r_i exp(-A_k + B_k h^2) len / pi
        // dh dphi (tau0 p(i, m) / cos(theta_m) dm, len = |M^-T m'| at most lenMax_i), so the proposal of mass
        //     W_ki = 2 a_i r_i lenMax_i exp(-A_k + B_k hi_k^2) (hi_k - lo_k)
        // bounds it, and a candidate is kept with exp(B_k (h^2 - hi_k^2)) (len / lenMax_i) g, g the weight of the target (the
        // cosine with the ray, the GAF of an entry, the heights allowed).
        private double[] proposalMass;
        private int[] proposalSlice, proposalType;
        private double proposalTotal;
        private double[] lenMax;

        private void PrepareWalk()
        {
            if (proposalMass != null)
            {
                return;
            }
            lenMax = new double[kMaxTypes];
            for (int i = 0; i < dist.n; ++i)
            {
                double a = dist.Minv[i, 0], b = dist.Minv[i, 1], d = dist.Minv[i, 3];
                double largest = 0.5 * (a + d) + Math.Sqrt(0.25 * (a - d) * (a - d) + b * b);
                lenMax[i] = Math.Max(1.0, largest);
            }
            int count = dist.n * (dist.n + 1) / 2;
            double[] mass = new double[count];
            proposalSlice = new int[count];
            proposalType = new int[count];
            int next = 0;
            double total = 0;
            for (int k = 0; k < dist.n; ++k)
            {
                for (int q = k; q < dist.n; ++q)
                {
                    int i = dist.order[q];
                    double w = 2 * dist.a[i] * dist.r[i] * lenMax[i] * Math.Exp(-dist.sliceA[k] + dist.sliceB[k] * dist.rHi[k] * dist.rHi[k]) *
                               (dist.rHi[k] - dist.rLo[k]);
                    mass[next] = w;
                    proposalSlice[next] = k;
                    proposalType[next] = i;
                    total += w;
                    ++next;
                }
            }
            proposalTotal = total;
            proposalMass = mass;
        }

        // prod_j Gdist_j(d, h / r_j): the probability that a ray from a point at the height h along d (upwards) leaves
        private double Leaves(mg.Vec3d d, double h)
        {
            double g = 1;
            for (int j = 0; j < dist.n; ++j)
            {
                g *= dist.GdistUni(j, d, h / dist.r[j]);
            }
            return g;
        }

        // The same for the two directions together (the union of the shadows)
        private double LeavesBoth(mg.Vec3d a, mg.Vec3d b, double h)
        {
            double g = 1;
            for (int j = 0; j < dist.n; ++j)
            {
                double hn = h / dist.r[j];
                if (hn >= 1)
                {
                    continue;
                }
                g *= dist.GdistBi(j, a, b, hn);
            }
            return g;
        }

        // The weight of the target at a point of height h with the normal m, for a ray that arrives against wIn
        private double TargetWeight(Heights heights, double limit, mg.Vec3d wIn, mg.Vec3d m, double h, mg.Vec3d wo)
        {
            double cosine = wIn.x * m.x + wIn.y * m.y + wIn.z * m.z;
            if (!(cosine > 0))
            {
                return 0;
            }
            switch (heights)
            {
            case Heights.Entry:
                return cosine * Leaves(wo, h);
            case Heights.Above:
                return h > limit ? cosine : 0;
            default:
                return h < limit ? cosine : 0;
            }
        }

        // The grain that the ray meets: its type, its normal and its height, and the number of candidates it took
        private bool SampleGrain(ref RNG rng, Heights heights, double limit, mg.Vec3d wIn, mg.Vec3d wo, out int type, out mg.Vec3d m, out double h, out int trials)
        {
            type = 0;
            m = default;
            h = 0;
            trials = 0;
            for (int attempt = 0; attempt < MaxTrials; ++attempt)
            {
                ++trials;
                double target = rng.Uniform<double>() * proposalTotal, below = 0;
                int chosen = proposalMass.Length - 1;
                for (int c = 0; c < proposalMass.Length; ++c)
                {
                    below += proposalMass[c];
                    if (target < below)
                    {
                        chosen = c;
                        break;
                    }
                }
                int k = proposalSlice[chosen], i = proposalType[chosen];
                double height = dist.rLo[k] + (dist.rHi[k] - dist.rLo[k]) * rng.Uniform<double>();
                double phi = 2 * Math.PI * rng.Uniform<double>();
                double accept = rng.Uniform<double>();
                double c0 = Math.Min(1.0, height / dist.r[i]), s0 = Math.Sqrt(Math.Max(0.0, 1 - c0 * c0));
                double mx = s0 * Math.Cos(phi), my = s0 * Math.Sin(phi);
                double x = dist.Minv[i, 0] * mx + dist.Minv[i, 2] * my, y = dist.Minv[i, 1] * mx + dist.Minv[i, 3] * my;
                double len = Math.Sqrt(x * x + y * y + c0 * c0);
                mg.Vec3d normal = new mg.Vec3d(x / len, y / len, c0 / len);
                double probability = Math.Exp(dist.sliceB[k] * (height * height - dist.rHi[k] * dist.rHi[k])) * (len / lenMax[i]) *
                                     TargetWeight(heights, limit, wIn, normal, height, wo);
                if (accept < probability)
                {
                    type = i;
                    m = normal;
                    h = height;
                    return true;
                }
            }
            return false;
        }

        // The choice between the specular and the diffuse reflection of a grain, for a ray with the cosine `cosine` on it
        private Float SpecularProbability(int i, Float cosine, out SampledSpectrum fresnel)
        {
            fresnel = GrainFresnel(i, cosine);
            if (spec[i] == PolyGrainSpecular.None)
            {
                return 0;
            }
            if (!(bool)Kd[i])
            {
                return 1;
            }
            return Clamp(fresnel.Average(), 0, 1);
        }

        private static Vector3f V(mg.Vec3d v) { return new Vector3f((Float)v.x, (Float)v.y, (Float)v.z); }

        // One walk from wo. With `connect`, what each event sends to wi is added to `sum` (times |cos wi|: a density when the
        // weights are those of `unit`); the events of the first scattering are left out unless `first`. Returns whether the
        // walk left the surface, with its direction, its weight and what it met.
        private bool Walk(Vector3f wo, ref RNG rng, TransportMode mode, bool unit, bool connect, bool first, Vector3f wi, ref SampledSpectrum sum,
                          out Vector3f direction, out SampledSpectrum weight, out bool specular, out Float etaScale, out int scatterings, Float ucFirst, Point2f uFirst, bool useFirst, GrainPolar pol = null)
        {
            PrepareWalk();
            scatterings = 0;
            direction = default;
            specular = false;
            etaScale = 1;
            weight = new SampledSpectrum(1f);
            mg.Vec3d wod = V3(wo), wid = V3(wi);
            // The event to come: on the bulk or on a grain, for a ray that arrives against wIn
            bool onBulk;
            Vector3f wIn = wo;
            Heights heights = Heights.Entry;
            double limit = 0;
            bool firstEvent = true;
            if (wo.z > 0)
            {
                double pores = (1 - dist.tauBar0) * dist.BulkVisUni(wod);
                onBulk = rng.Uniform<double>() < pores;
            }
            else
            {
                onBulk = true;
            }
            for (int order = 0; order < maxOrder; ++order)
            {
                bool counted = connect && (first || !firstEvent);
                scatterings = order + 1;
                Vector3f w;
                double height;
                if (onBulk)
                {
                    // ---- the bulk
                    bool fromAbove = wIn.z > 0;
                    if (counted && wi.z != 0)
                    {
                        double leaves = 1;
                        if (wi.z > 0)
                        {
                            if (fromAbove)
                            {
                                leaves = (firstEvent && wo.z > 0) ? dist.BulkVisBi(wod, wid) / Math.Max(dist.BulkVisUni(wod), 1e-300) : dist.BulkVisUni(wid);
                            }
                            else
                            {
                                // (through the bulk from below: the point is under a pore, or under a grain, which is opaque)
                                leaves = (1 - dist.tauBar0) * dist.BulkVisUni(wid);
                            }
                        }
                        if (unit)
                        {
                            sum += weight * (bulk.PDF(wIn, wi, mode, BxDFReflTransFlags.All) * (Float)leaves);
                        }
                        else
                        {
                            SampledSpectrum value = bulk.f(wIn, wi, mode) * (AbsCosTheta(wi) * (Float)leaves);
                            sum += weight * value;
                            if (pol != null && (bool)value)
                            {
                                pol.Add(BulkMatrix(pol, wIn, wi, mode, value), true);
                            }
                        }
                    }
                    Float uc = useFirst && firstEvent ? ucFirst : rng.Uniform<Float>();
                    Point2f u = useFirst && firstEvent ? uFirst : new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
                    bool bulkPolarised = false;
                    Attenuation bulkMatrix = default;
                    pstd.optional<BSDFSample> sampled = pol != null && bulk.HasMuellerMatrix()
                        ? bulk.Sample_f(pol.bsdf, wIn, uc, u, mode, BxDFReflTransFlags.All, out bulkPolarised, out bulkMatrix)
                        : bulk.Sample_f(wIn, uc, u, mode, BxDFReflTransFlags.All);
                    if (!sampled || !(bool)sampled.value().f || sampled.value().pdf == 0 || sampled.value().wi.z == 0)
                    {
                        return false;
                    }
                    BSDFSample bs = sampled.value();
                    if (!unit)
                    {
                        SampledSpectrum factor = bs.f * (AbsCosTheta(bs.wi) / bs.pdf);
                        weight *= factor;
                        if (pol != null)
                        {
                            pol.Follow(bulkPolarised ? bulkMatrix * factor : Attenuation.Lambert(factor));
                        }
                    }
                    specular = firstEvent && bs.IsSpecular();
                    if (bs.IsTransmission())
                    {
                        etaScale *= bs.eta;
                    }
                    w = bs.wi;
                    if (w.z < 0)
                    {
                        // below the surface: nothing is there
                        direction = w;
                        return true;
                    }
                    double free;
                    if (fromAbove)
                    {
                        free = (firstEvent && wo.z > 0) ? dist.BulkVisBi(wod, V3(w)) / Math.Max(dist.BulkVisUni(wod), 1e-300) : dist.BulkVisUni(V3(w));
                    }
                    else
                    {
                        if (rng.Uniform<double>() < dist.tauBar0)
                        {
                            return false;  // under a grain
                        }
                        free = dist.BulkVisUni(V3(w));
                    }
                    if (rng.Uniform<double>() < free)
                    {
                        direction = w;
                        return true;
                    }
                    specular = false;
                    height = 0;
                }
                else
                {
                    // ---- a grain
                    mg.Vec3d wInd = V3(wIn);
                    int type, trials;
                    mg.Vec3d md;
                    if (!SampleGrain(ref rng, heights, limit, wInd, wod, out type, out md, out height, out trials))
                    {
                        return false;
                    }
                    Vector3f m = V(md);
                    bool entry = heights == Heights.Entry;
                    if (counted && wi.z > 0)
                    {
                        // 1 / (the grains that the ray can meet, weighted): known for a ray from outside, estimated otherwise
                        double inverse = entry ? 1 / (wo.z * (1 - (1 - dist.tauBar0) * dist.BulkVisUni(wod))) : trials / proposalTotal;
                        // the specular reflection of every type, at the half vector
                        Vector3f half = wIn + wi;
                        if (LengthSquared(half) > 0)
                        {
                            half = Normalize(half);
                            mg.Vec3d hd = V3(half);
                            Float cosine = Dot(wIn, half);
                            if (cosine > 0 && half.z > 0)
                            {
                                for (int i = 0; i < dist.n; ++i)
                                {
                                    if (spec[i] == PolyGrainSpecular.None)
                                    {
                                        continue;
                                    }
                                    double hh = dist.Height(i, hd);
                                    double g = TargetWeight(heights, limit, wInd, hd, hh, wod);
                                    if (!(g > 0))
                                    {
                                        continue;
                                    }
                                    double leaves = entry ? LeavesBoth(wod, wid, hh) / Math.Max(Leaves(wod, hh), 1e-300) : Leaves(wid, hh);
                                    double density = dist.tauBar0 * dist.PTypeNormal(i, hd) / hd.z * g * inverse / (4 * cosine) * leaves;
                                    SampledSpectrum fresnel;
                                    Float pSpecular = SpecularProbability(i, cosine, out fresnel);
                                    sum += unit ? weight * ((Float)density * pSpecular) : weight * fresnel * (Float)density;
                                    if (pol != null && density > 0)
                                    {
                                        pol.Add(GrainMatrix(pol, i, wIn, wi, half) * new SampledSpectrum((Float)density), true);
                                    }
                                }
                            }
                        }
                        // the diffuse reflection of the grain that was met
                        Float cosIn = Dot(wIn, m), cosOut = Dot(wi, m);
                        if (cosOut > 0 && (bool)Kd[type])
                        {
                            double leaves = entry ? LeavesBoth(wod, wid, height) / Math.Max(Leaves(wod, height), 1e-300) : Leaves(wid, height);
                            SampledSpectrum fresnel;
                            Float pSpecular = SpecularProbability(type, cosIn, out fresnel);
                            if (unit)
                            {
                                sum += weight * ((1 - pSpecular) * cosOut * InvPi * (Float)leaves);
                            }
                            else
                            {
                                SampledSpectrum value = Kd[type] * (new SampledSpectrum(1f) - fresnel) * (new SampledSpectrum(1f) - GrainFresnel(type, cosOut)) *
                                                        (cosOut * InvPi * (Float)leaves);
                                sum += weight * value;
                                if (pol != null && (bool)value)
                                {
                                    pol.Add(Attenuation.Lambert(value), true);
                                }
                            }
                        }
                    }
                    // the reflection that is followed
                    {
                        Float cosIn = Dot(wIn, m);
                        SampledSpectrum fresnel;
                        Float pSpecular = SpecularProbability(type, cosIn, out fresnel);
                        if (pSpecular == 0 && !(bool)Kd[type])
                        {
                            return false;  // a black grain
                        }
                        if (rng.Uniform<Float>() < pSpecular)
                        {
                            w = -wIn + 2 * cosIn * m;
                            if (!unit)
                            {
                                weight *= fresnel / pSpecular;
                                if (pol != null)
                                {
                                    pol.Follow(GrainMatrix(pol, type, wIn, w, m) * new SampledSpectrum(1 / pSpecular));
                                }
                            }
                        }
                        else
                        {
                            Vector3f local = SampleCosineHemisphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                            Vector3f tangent, bitangent;
                            CoordinateSystem(m, &tangent, &bitangent);
                            w = local.x * tangent + local.y * bitangent + local.z * m;
                            if (!unit)
                            {
                                SampledSpectrum factor = Kd[type] * (new SampledSpectrum(1f) - fresnel) * (new SampledSpectrum(1f) - GrainFresnel(type, Dot(w, m))) /
                                                         (1 - pSpecular);
                                weight *= factor;
                                if (pol != null)
                                {
                                    pol.Follow(Attenuation.Lambert(factor));
                                }
                            }
                        }
                        if (!(bool)weight || w.z == 0 || IsNaN(w.x + w.y + w.z))
                        {
                            return false;
                        }
                        w = Normalize(w);
                    }
                    if (w.z > 0)
                    {
                        mg.Vec3d wd = V3(w);
                        double free = entry ? LeavesBoth(wod, wd, height) / Math.Max(Leaves(wod, height), 1e-300) : Leaves(wd, height);
                        if (rng.Uniform<double>() < free)
                        {
                            direction = w;
                            return true;
                        }
                    }
                }
                // ---- the next event, for the ray that goes on along w from the height `height`
                firstEvent = false;
                wIn = -w;
                limit = height;
                if (w.z > 0)
                {
                    onBulk = false;
                    heights = Heights.Above;
                }
                else
                {
                    mg.Vec3d back = V3(-w);
                    double reaches = dist.BulkVisUni(back) / Math.Max(Leaves(back, height), 1e-300);
                    // The grains that cut the ray between the height of the point and the base, exactly: their centres lie within
                    // a radius of the segment, an area that is the shadow of the whole ray from the base less that of the ray from
                    // the point, both counted with the disc that the point itself keeps free. The ratio above leaves the two discs
                    // out, pi r^2 at the base and pi (r^2 - h^2) at the point: the difference, pi h^2 for each type, is restored.
                    for (int j = 0; j < dist.n; ++j)
                    {
                        double hn = Math.Min(1.0, height / dist.r[j]);
                        reaches *= Math.Exp(ff.Log1p(-dist.tau[j]) * hn * hn);
                    }
                    onBulk = rng.Uniform<double>() < reaches;
                    heights = Heights.Below;
                }
            }
            return false;
        }

        public SampledSpectrum WalkF(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            return WalkF(default, wo, wi, mode, false, out bool _, out Attenuation _);
        }

        // With `keep`, the Mueller matrix too (polygrain_polarised.cs)
        private SampledSpectrum WalkF(BSDF bsdf, Vector3f wo, Vector3f wi, TransportMode mode, bool keep, out bool polarised, out Attenuation matrix)
        {
            polarised = false;
            matrix = default;
            if (wo.z == 0 || wi.z == 0)
            {
                return new SampledSpectrum(0f);
            }
            TransportMode other = mode == TransportMode.Radiance ? TransportMode.Importance : TransportMode.Radiance;
            GrainPolar pol = keep ? new GrainPolar(bsdf, mode == TransportMode.Radiance, wo, wi) : null;
            // The first scattering: the BSDF of the paper (from below, the walk has it)
            SampledSpectrum result = new SampledSpectrum(0f);
            bool above = wo.z > 0;
            if (above)
            {
                SampledSpectrum bulkPart = new SampledSpectrum(0f), grains = new SampledSpectrum(0f);
                SampledSpectrum fb = bulk.f(wo, wi, mode);
                if ((bool)fb)
                {
                    bulkPart = BulkVis(wo, wi) * fb;
                }
                if (wi.z > 0)
                {
                    grains = (Float)dist.tauBar0 * GrainF(wo, wi);
                }
                result = bulkPart + grains;
                if (keep)
                {
                    FirstMatrices(pol, wo, wi, mode, bulkPart, grains);
                }
            }
            if (maxOrder > 1 || !above)
            {
                if (reciprocal && above && wi.z > 0)
                {
                    // the later scatterings from both ends, averaged: reciprocal
                    GrainPolar back = keep ? new GrainPolar(bsdf, mode != TransportMode.Radiance, wi, wo) : null;
                    result += 0.5f * (Later(wo, wi, mode, false, pol, 0.5f) + Later(wi, wo, other, false, back, 0.5f));
                    if (keep)
                    {
                        pol.Merge(back);
                    }
                }
                else
                {
                    result += Later(wo, wi, mode, !above, pol, 1);
                }
            }
            if (keep && pol.hasSum)
            {
                polarised = PolarisedFacet.Normalise(pol.sum, result, new SampledSpectrum(1f), out matrix);
            }
            return result;
        }

        // What the walks from wo send to wi after the first scattering (or from it, with `first`), as a BSDF
        private SampledSpectrum Later(Vector3f wo, Vector3f wi, TransportMode mode, bool first, GrainPolar pol, Float share)
        {
            Attenuation before = default;
            bool hadSum = false;
            if (pol != null)
            {
                // (the sum so far, the first scattering, is not to be scaled as the walks are)
                before = pol.sum;
                hadSum = pol.hasSum;
                pol.hasSum = false;
            }
            RNG rng = new RNG(Hash(GetOptions().seed, wo, 0x504f4c59UL), Hash(wi));
            SampledSpectrum sum = new SampledSpectrum(0f);
            for (int s = 0; s < nSamples; ++s)
            {
                if (pol != null)
                {
                    pol.hasChain = false;
                }
                Walk(wo, ref rng, mode, false, true, first, wi, ref sum, out Vector3f _, out SampledSpectrum _, out bool _, out Float _, out int _, 0, default, false, pol);
            }
            if (pol != null)
            {
                pol.Scale(new SampledSpectrum(share / (nSamples * AbsCosTheta(wi))));
                if (hadSum)
                {
                    if (pol.hasSum)
                    {
                        pol.sum += before;
                    }
                    else
                    {
                        pol.sum = before;
                        pol.hasSum = true;
                    }
                }
            }
            for (int c = 0; c < NSpectrumSamples; ++c)
            {
                if (!Float.IsFinite(sum[c]) || sum[c] < 0)
                {
                    sum[c] = 0;
                }
            }
            return sum / (nSamples * AbsCosTheta(wi));
        }

        public Float WalkPDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if (wo.z == 0 || wi.z == 0)
            {
                return 0;
            }
            bool reflect = wo.z * wi.z > 0;
            if ((sampleFlags & (reflect ? BxDFReflTransFlags.Reflection : BxDFReflTransFlags.Transmission)) == 0)
            {
                return 0;
            }
            RNG rng = new RNG(Hash(GetOptions().seed, wi, 0x504f4c5950UL), Hash(wo));
            SampledSpectrum sum = new SampledSpectrum(0f);
            Walk(wo, ref rng, mode, true, true, true, wi, ref sum, out Vector3f _, out SampledSpectrum _, out bool _, out Float _, out int _, 0, default, false);
            Float density = sum[0];
            if (reciprocal && wo.z > 0 && maxOrder > 1)
            {
                density *= 1 - ReverseShare;
                if (wi.z > 0)
                {
                    density += ReverseShare * wi.z * InvPi;
                }
            }
            // Strictly positive: the integrator divides by this (pdfIsProportional)
            return Float.IsFinite(density) ? std.max<Float>(density, 1e-6f) : 1e-6f;
        }

        public pstd.optional<BSDFSample> WalkSample(Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            return WalkSample(default, wo, uc, u, mode, sampleFlags, false, out bool _, out Attenuation _);
        }

        private pstd.optional<BSDFSample> WalkSample(BSDF bsdf, Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags, bool keep,
                                                     out bool polarised, out Attenuation matrix)
        {
            polarised = false;
            matrix = default;
            if (wo.z == 0)
            {
                return default;
            }
            RNG rng = new RNG(Hash(GetOptions().seed, wo), Hash(uc, u));
            SampledSpectrum none = new SampledSpectrum(0f);
            Vector3f wi;
            SampledSpectrum weight;
            bool specular;
            Float etaScale;
            int scatterings;
            bool symmetric = reciprocal && wo.z > 0 && maxOrder > 1;
            if (symmetric)
            {
                // The half of the later scatterings that is walked from the other end has no walk from wo that samples it: a
                // share of the samples takes a direction by the cosine and evaluates it
                if (uc < ReverseShare)
                {
                    if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0)
                    {
                        return default;
                    }
                    wi = SampleCosineHemisphere(u);
                    if (wi.z <= 0)
                    {
                        return default;
                    }
                    GrainPolar back = keep ? new GrainPolar(bsdf, mode != TransportMode.Radiance, wi, wo) : null;
                    SampledSpectrum reverse = Later(wi, wo, mode == TransportMode.Radiance ? TransportMode.Importance : TransportMode.Radiance, false, back, 1);
                    if (!(bool)reverse)
                    {
                        return default;
                    }
                    if (keep && back.hasSum)
                    {
                        polarised = PolarisedFacet.Normalise(back.sum, reverse, new SampledSpectrum(1f), out matrix);
                    }
                    return new BSDFSample(reverse * (0.5f / ReverseShare), wi, wi.z * InvPi, BxDFFlags.GlossyReflection, 1, true);
                }
                uc = (uc - ReverseShare) / (1 - ReverseShare);
            }
            GrainPolar pol = keep ? new GrainPolar(bsdf, mode == TransportMode.Radiance, wo, wo) : null;
            if (!Walk(wo, ref rng, mode, false, false, true, default, ref none, out wi, out weight, out specular, out etaScale, out scatterings, uc, u, true, pol))
            {
                return default;
            }
            if (keep && pol.hasChain)
            {
                polarised = PolarisedFacet.Normalise(pol.chain, weight, new SampledSpectrum(1f), out matrix);
            }
            if (symmetric)
            {
                weight *= (scatterings > 1 && wi.z > 0 ? 0.5f : 1f) / (1 - ReverseShare);
            }
            if (wi.z == 0 || !(bool)weight)
            {
                return default;
            }
            bool reflect = wo.z * wi.z > 0;
            if ((sampleFlags & (reflect ? BxDFReflTransFlags.Reflection : BxDFReflTransFlags.Transmission)) == 0)
            {
                return default;
            }
            for (int c = 0; c < NSpectrumSamples; ++c)
            {
                if (!Float.IsFinite(weight[c]) || weight[c] < 0)
                {
                    return default;
                }
            }
            SampledSpectrum value = weight / AbsCosTheta(wi);
            if (specular)
            {
                // the mirror of the bulk seen through the pores, and nothing else: a specular sample
                return new BSDFSample(value, wi, 1, reflect ? BxDFFlags.SpecularReflection : BxDFFlags.SpecularTransmission, reflect ? 1 : etaScale, false);
            }
            return new BSDFSample(value, wi, 1, reflect ? BxDFFlags.GlossyReflection : BxDFFlags.GlossyTransmission, reflect ? 1 : etaScale, true);
        }

        private const int MaxTrials = 4096;
        private const Float ReverseShare = 0.2f;
        // The later scatterings walked from both directions and averaged (reciprocal); false: from wo alone
        public bool reciprocal = true;
    }
}
