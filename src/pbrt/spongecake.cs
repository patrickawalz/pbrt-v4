// (not in pbrt-v4) SpongeCake: layers of microflakes.
//
// Wang, Jin, Hašan, Yan, "SpongeCake: A Layered Microflake Surface Appearance Model" (ACM Transactions on Graphics 42(1),
// 2022). A surface is a stack of flat layers, each a homogeneous medium of microflakes: tiny two-sided mirrors whose normals
// follow the SGGX distribution (Heitz et al. 2015), flattened ellipsoids for a surface-like layer and long thin ones for
// fibres. There is no interface between the layers or above them, so light crosses the boundaries in a straight line.
//
// From the paper, as it is:
//   - the single scattering of the layers in closed form (its equations 10 to 12 and 16, added over the layers with the
//     attenuation of the layers on the way: section 4.4);
//   - the light that crosses the layers without meeting a flake (the delta transmission, section 4.5) and a substrate under
//     the layers (the same section);
//   - Henyey-Greenstein layers beside the two kinds of flakes.
//
// Not from the paper:
//   - Multiple scattering. The paper replaces it by a second single-scattering lobe and a Lambertian one whose parameters a
//     neural network gives, which is approximate and needs the trained network. Here it is the random walk in the layers
//     that the paper takes as its ground truth, added to the closed form of the first scattering: at every scattering after
//     the first the walk is joined to the other direction, and the join is exact, because nothing bends the light on its
//     way out (the attenuation of the layers along a straight line). Unbiased; "string scattering" "single" is the closed
//     form alone.
//   - The reflectance of a flake. The paper has Schlick's approximation with a reflectance at normal incidence; here it is
//     the Fresnel reflectance of the conductor that has that reflectance at normal incidence (as pbrt does for a conductor
//     given by a colour), so that the two polarisations exist: a flake is a mirror with their Fresnel matrix.
//   - The density used to weight the samples is the paper's (the phase function of the layer that the light first meets, by
//     the probability of meeting it), with a constant part added for what is scattered more than once. The samples
//     themselves are the walk.

using System;
using System.Collections.Generic;
using Attenuation = pbrt.PolarisedLightSystem.Attenuation;

namespace pbrt
{
    public enum FlakeKind
    {
        Surface,
        Fiber,
        HenyeyGreenstein
    }

    // One layer of the cake
    public unsafe struct FlakeLayer
    {
        public FlakeKind kind;
        // The roughness of the flakes; the axis of the distribution (the normal of a surface-like layer, the direction of the
        // fibres), of length 1; the optical thickness (thickness times density of flakes); the asymmetry of a
        // Henyey-Greenstein layer
        public Float alpha;
        public Vector3f axis;
        public Float thickness;
        public Float g;
        // The albedo of a scattering, and the extinction coefficient of the conductor (of index 1) that the flakes are made of
        public SampledSpectrum albedo;
        public SampledSpectrum flakeK;

        // The SGGX matrix is across (I - axis axis^T) + along axis axis^T
        private Float Across() { return kind == FlakeKind.Surface ? alpha * alpha : 1; }
        private Float Along() { return kind == FlakeKind.Surface ? 1 : alpha * alpha; }

        // S v
        private Vector3f Times(Vector3f v)
        {
            return Across() * v + (Along() - Across()) * pbrt.Dot(v, axis) * axis;
        }

        // The projected area of the flakes seen along w (the extinction for a unit density): sqrt(w^T S w)
        public Float Sigma(Vector3f w)
        {
            if (kind == FlakeKind.HenyeyGreenstein)
            {
                return 1;
            }
            Float along = pbrt.Dot(w, axis);
            return pbrt.SafeSqrt(Across() + (Along() - Across()) * along * along);
        }

        // The distribution of the normals of the flakes: 1 / (pi sqrt(det S) (m^T S^-1 m)^2)
        public Float D(Vector3f m)
        {
            Float along = pbrt.Dot(m, axis);
            Float q = 1 / Across() + (1 / Along() - 1 / Across()) * along * along;
            return 1 / (pbrt.Pi * Across() * pbrt.SafeSqrt(Along()) * q * q);
        }

        // The phase function from a to b (both point away from the flake): D(h) / (4 sigma(a))
        public Float Phase(Vector3f a, Vector3f b)
        {
            if (kind == FlakeKind.HenyeyGreenstein)
            {
                return pbrt.HenyeyGreenstein(pbrt.Dot(a, b), g);
            }
            Vector3f h = a + b;
            if (pbrt.LengthSquared(h) == 0)
            {
                return 0;
            }
            return D(pbrt.Normalize(h)) / (4 * Sigma(a));
        }

        // A normal of the flakes that a sees, with the density D(m) <a, m> / sigma(a) (Heitz et al. 2015, their listing)
        public Vector3f SampleNormal(Vector3f a, Point2f u)
        {
            Float r = pbrt.SafeSqrt(u.x), phi = 2 * pbrt.Pi * u.y;
            Float x = r * std.cos(phi), y = r * std.sin(phi), z = pbrt.SafeSqrt(1 - x * x - y * y);
            Vector3f wk, wj;
            pbrt.CoordinateSystem(a, &wk, &wj);
            Vector3f sk = Times(wk), sj = Times(wj), si = Times(a);
            Float skk = pbrt.Dot(wk, sk), sjj = pbrt.Dot(wj, sj), sii = pbrt.Dot(a, si);
            Float skj = pbrt.Dot(wk, sj), ski = pbrt.Dot(wk, si), sji = pbrt.Dot(wj, si);
            Float determinant = std.abs(skk * sjj * sii - skj * skj * sii - ski * ski * sjj - sji * sji * skk + 2 * skj * ski * sji);
            Float root = pbrt.SafeSqrt(determinant), inverse = 1 / pbrt.SafeSqrt(sii), part = pbrt.SafeSqrt(sjj * sii - sji * sji);
            Vector3f mk = new Vector3f(root / part, 0, 0);
            Vector3f mj = new Vector3f(-inverse * (ski * sji - skj * sii) / part, inverse * part, 0);
            Vector3f mi = new Vector3f(inverse * ski, inverse * sji, inverse * sii);
            Vector3f local = pbrt.Normalize(x * mk + y * mj + z * mi);
            return pbrt.Normalize(local.x * wk + local.y * wj + local.z * a);
        }

        // The reflectances of a flake for the two polarisations and their phases, for light at the cosine `cosine` to its
        // normal
        public void Fresnel(Float cosine, out SampledSpectrum Fs, out SampledSpectrum Fp, out SampledSpectrum Ds, out SampledSpectrum Dp)
        {
            Fs = new SampledSpectrum(0f);
            Fp = new SampledSpectrum(0f);
            Ds = new SampledSpectrum(0f);
            Dp = new SampledSpectrum(0f);
            for (int i = 0; i < pbrt.NSpectrumSamples; ++i)
            {
                Float fs, fp, ds, dp;
                pbrt.FrComplexPolarised(std.abs(cosine), new pstd.complex(1, flakeK[i]), out fs, out fp, out ds, out dp);
                Fs[i] = fs;
                Fp[i] = fp;
                Ds[i] = ds;
                Dp[i] = dp;
            }
        }

        // What a scattering from a to b keeps of the light: the albedo, times the reflectance of the flake that does it
        public SampledSpectrum Kept(Vector3f a, Vector3f b)
        {
            if (kind == FlakeKind.HenyeyGreenstein)
            {
                return albedo;
            }
            Vector3f h = a + b;
            if (pbrt.LengthSquared(h) == 0)
            {
                return new SampledSpectrum(0f);
            }
            Fresnel(pbrt.Dot(a, pbrt.Normalize(h)), out SampledSpectrum Fs, out SampledSpectrum Fp, out SampledSpectrum _, out SampledSpectrum _);
            return albedo * 0.5f * (Fs + Fp);
        }
    }

    // SpongeCakeBxDF Definition
    public sealed unsafe class SpongeCakeBxDF : BxDF
    {
        public SpongeCakeBxDF() { }

        // layers[0] is the top; substrate: what is under the layers (null: nothing, and the layers let light through);
        // single: the first scattering only; delta: with the light that meets no flake
        public SpongeCakeBxDF(FlakeLayer[] layers, BxDF substrate, bool single, bool delta, int maxDepth, int nSamples)
        {
            this.layers = layers;
            this.substrate = substrate;
            this.single = single;
            this.delta = delta;
            this.maxDepth = maxDepth;
            this.nSamples = nSamples;
        }

        public override BxDFFlags Flags()
        {
            BxDFFlags flags = BxDFFlags.GlossyReflection;
            if (substrate != null && IsSpecular(substrate.Flags()))
            {
                flags = flags | BxDFFlags.SpecularReflection;
            }
            if (substrate == null)
            {
                flags = flags | BxDFFlags.GlossyTransmission;
                if (delta)
                {
                    flags = flags | BxDFFlags.SpecularTransmission;
                }
            }
            return flags;
        }

        public override bool HasMuellerMatrix() { return true; }

        public override void Regularize()
        {
            if (substrate != null)
            {
                substrate.Regularize();
            }
        }

        // What crosses the whole of the layer k along w
        private Float Across(int k, Vector3f w)
        {
            return std.exp(-layers[k].thickness * layers[k].Sigma(w) / AbsCosTheta(w));
        }

        // What reaches the outside along w (which points away) from the depth `depth` under the top of the layer k: nothing
        // downwards when there is a substrate
        private Float Leaves(int k, Float depth, Vector3f w)
        {
            if (w.z < 0 && substrate != null)
            {
                return 0;
            }
            return Reaches(k, depth, w);
        }

        // What reaches the top of the layers along w, or their bottom, from that depth
        private Float Reaches(int k, Float depth, Vector3f w)
        {
            Float span = w.z > 0 ? depth : layers[k].thickness - depth;
            Float left = std.exp(-span * layers[k].Sigma(w) / AbsCosTheta(w));
            for (int j = w.z > 0 ? k - 1 : k + 1; j >= 0 && j < layers.Length; j += w.z > 0 ? -1 : 1)
            {
                left *= Across(j, w);
            }
            return left;
        }

        // The matrix of the scattering from a to b by a flake of the layer, with a first element of 1
        private Attenuation FlakeMatrix(BSDF bsdf, Float side, FlakeLayer layer, Vector3f a, Vector3f b, bool fromCamera)
        {
            Vector3f h = a + b;
            if (layer.kind == FlakeKind.HenyeyGreenstein || LengthSquared(h) == 0)
            {
                return Attenuation.Lambert(new SampledSpectrum(1f));
            }
            h = Normalize(h);
            layer.Fresnel(Dot(a, h), out SampledSpectrum Fs, out SampledSpectrum Fp, out SampledSpectrum Ds, out SampledSpectrum Dp);
            SampledSpectrum scale = new SampledSpectrum(0f);
            bool any = false;
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                Float mean = 0.5f * (Fs[i] + Fp[i]);
                scale[i] = mean > 0 ? 1 / mean : 0;
                any |= mean > 0;
            }
            if (!any)
            {
                return Attenuation.Lambert(new SampledSpectrum(1f));
            }
            return Attenuation.Fresnel(scale, Fs, Fp, Ds, Dp)
                .Scattered(bsdf.LocalToRender(side * a), bsdf.LocalToRender(side * b), bsdf.LocalToRender(side * h), fromCamera);
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
            if (polarisedInside)
            {
                return Inside(wo, wi, mode);
            }
            return Evaluate(wo, wi, mode, default, false, out Attenuation _, out bool _);
        }

        public override SampledSpectrum f(BSDF bsdf, Vector3f wo, Vector3f wi, TransportMode mode, out bool polarised, out Attenuation matrix)
        {
            polarised = false;
            matrix = default;
            SampledSpectrum value = Evaluate(wo, wi, mode, bsdf, true, out Attenuation matrices, out bool any);
            if (any)
            {
                polarised = PolarisedFacet.Normalise(matrices, value * nSamples, new SampledSpectrum(1f), out matrix);
            }
            return value;
        }

        // The value and, with `keep`, the sum of the matrices of its paths, each times its scalar contribution times nSamples
        private SampledSpectrum Evaluate(Vector3f wo, Vector3f wi, TransportMode mode, BSDF bsdf, bool keep, out Attenuation matrices, out bool any)
        {
            matrices = default;
            any = false;
            Float side = 1;
            if (substrate != null && wo.z < 0)
            {
                wo = -wo;
                wi = -wi;
                side = -1;
            }
            if (wo.z == 0 || wi.z == 0 || (substrate != null && wi.z < 0))
            {
                return new SampledSpectrum(0f);
            }
            bool fromCamera = mode == TransportMode.Radiance;
            Vector3f directionExit = default, directionEnter = default;
            if (keep)
            {
                Vector3f woRender = bsdf.LocalToRender(side * wo), wiRender = bsdf.LocalToRender(side * wi);
                directionExit = fromCamera ? woRender : wiRender;
                directionEnter = fromCamera ? -wiRender : -woRender;
            }
            int count = layers.Length;
            bool fromTop = wo.z > 0, toTop = wi.z > 0;
            Float cosO = AbsCosTheta(wo), cosI = AbsCosTheta(wi);

            // The first scattering, in closed form: for each layer, the integral over the depth of the scattering of what
            // reaches it along one direction and leaves it along the other (the paper's equations 10, 12 and 16), times what
            // the layers on the way let through
            SampledSpectrum sum = new SampledSpectrum(0f);
            Float reachesO = 1;
            for (int step = 0; step < count; ++step)
            {
                int k = fromTop ? step : count - 1 - step;
                FlakeLayer layer = layers[k];
                Float lambdaO = layer.Sigma(wo) / cosO, lambdaI = layer.Sigma(wi) / cosI, thickness = layer.thickness;
                Float reachesI = 1;
                for (int j = toTop ? k - 1 : k + 1; j >= 0 && j < count; j += toTop ? -1 : 1)
                {
                    reachesI *= Across(j, wi);
                }
                Float depths;
                if (fromTop == toTop)
                {
                    depths = (1 - std.exp(-thickness * (lambdaO + lambdaI))) / (lambdaO + lambdaI);
                }
                else if (std.abs(lambdaO - lambdaI) < 1e-4f * (lambdaO + lambdaI))
                {
                    depths = thickness * std.exp(-thickness * lambdaO);
                }
                else
                {
                    depths = (std.exp(-thickness * lambdaI) - std.exp(-thickness * lambdaO)) / (lambdaO - lambdaI);
                }
                // (the reduced phase function: the extinction along wo times the phase function, D(h) / 4 for flakes)
                SampledSpectrum scattered = layer.Kept(wo, wi) * (layer.Sigma(wo) * layer.Phase(wo, wi) * depths * reachesO * reachesI / (cosO * cosI));
                if ((bool)scattered)
                {
                    sum += scattered;
                    if (keep)
                    {
                        StackBxDF.Accumulate(ref matrices, ref any, FlakeMatrix(bsdf, side, layer, wo, wi, fromCamera), scattered * nSamples, directionExit,
                                             directionEnter);
                    }
                }
                reachesO *= Across(k, wo);
            }
            // The substrate, seen through the layers
            if (substrate != null)
            {
                Float through = reachesO;
                for (int j = 0; j < count; ++j)
                {
                    through *= Across(j, wi);
                }
                SampledSpectrum under = substrate.f(wo, wi, mode) * through;
                if ((bool)under)
                {
                    sum += under;
                    if (keep)
                    {
                        StackBxDF.Accumulate(ref matrices, ref any, StackBxDF.EvaluatedMatrix(bsdf, side, substrate, wo, wi, mode), under * nSamples,
                                             directionExit, directionEnter);
                    }
                }
            }
            if (single)
            {
                return sum;
            }

            // A substrate that is a mirror has no value: the light that a flake sends down to it and that it reflects to wi is
            // joined at the flake, along the one direction that the mirror turns into wi
            bool mirror = substrate != null && !IsNonSpecular(substrate.Flags());
            Vector3f mirrorFrom = new Vector3f(-wi.x, -wi.y, wi.z), mirrorDown = new Vector3f(wi.x, wi.y, -wi.z);
            SampledSpectrum mirrorKeeps = new SampledSpectrum(0f);
            Float mirrorUc = 0.5f;
            Point2f mirrorU = new Point2f(0.5f, 0.5f);
            if (mirror)
            {
                pstd.optional<BSDFSample> turned = substrate.Sample_f(mirrorFrom, mirrorUc, mirrorU, mode, BxDFReflTransFlags.Reflection);
                mirror = (bool)turned && turned.value().pdf > 0 && turned.value().IsSpecular();
                if (mirror)
                {
                    Float through = 1;
                    for (int j = 0; j < count; ++j)
                    {
                        through *= Across(j, wi);
                    }
                    mirrorKeeps = turned.value().f * (AbsCosTheta(turned.value().wi) / turned.value().pdf * through);
                }
            }

            // What is scattered more than once: a walk from wo, joined to wi at every event after the first (a scattering by a
            // flake or a reflection of the substrate)
            SampledSpectrum more = new SampledSpectrum(0f);
            RNG rng = new RNG(Hash(GetOptions().seed, wo), Hash(wi));
            Float r() { return std.min<Float>(rng.Uniform<Float>(), OneMinusEpsilon); }
            for (int s = 0; s < nSamples; ++s)
            {
                Vector3f w = -wo;
                int k = fromTop ? 0 : count - 1;
                Float depth = fromTop ? 0 : layers[k].thickness;
                SampledSpectrum beta = new SampledSpectrum(1f);
                Attenuation chain = default;
                bool hasChain = false;
                int events = 0;
                for (int step = 0; step < maxDepth; ++step)
                {
                    if (keep && hasChain)
                    {
                        StackBxDF.Rebalance(ref beta, ref chain);
                    }
                    if (events > 3 && beta.MaxComponentValue() < 0.25f)
                    {
                        Float q = std.max<Float>(0, 1 - beta.MaxComponentValue());
                        if (r() < q)
                        {
                            break;
                        }
                        beta /= 1 - q;
                    }
                    if (w.z == 0)
                    {
                        break;
                    }
                    FlakeLayer layer = layers[k];
                    bool down = w.z < 0;
                    Float flight = SampleExponential(r(), layer.Sigma(w) / AbsCosTheta(w));
                    if (flight < (down ? layer.thickness - depth : depth))
                    {
                        // A flake
                        depth += down ? flight : -flight;
                        ++events;
                        if (mirror)
                        {
                            SampledSpectrum viaMirror = beta * layer.Kept(-w, mirrorDown) * mirrorKeeps *
                                                        (layer.Phase(-w, mirrorDown) * Reaches(k, depth, mirrorDown) / cosI);
                            if ((bool)viaMirror)
                            {
                                more += viaMirror;
                                if (keep)
                                {
                                    Attenuation path = PolarisedFacet.Compose(chain, hasChain, FlakeMatrix(bsdf, side, layer, -w, mirrorDown, fromCamera),
                                                                              fromCamera);
                                    path = PolarisedFacet.Compose(path, true,
                                                                  StackBxDF.SampledMatrix(bsdf, side, substrate, mirrorFrom, wi, mirrorUc, mirrorU, mode,
                                                                                          BxDFReflTransFlags.Reflection, fromCamera, false),
                                                                  fromCamera);
                                    StackBxDF.Accumulate(ref matrices, ref any, path, viaMirror, directionExit, directionEnter);
                                }
                            }
                        }
                        if (events >= 2)
                        {
                            Float leaves = Leaves(k, depth, wi);
                            SampledSpectrum joined = beta * layer.Kept(-w, wi) * (layer.Phase(-w, wi) * leaves / cosI);
                            if ((bool)joined)
                            {
                                more += joined;
                                if (keep)
                                {
                                    StackBxDF.Accumulate(ref matrices, ref any,
                                                         PolarisedFacet.Compose(chain, hasChain, FlakeMatrix(bsdf, side, layer, -w, wi, fromCamera), fromCamera),
                                                         joined, directionExit, directionEnter);
                                }
                            }
                        }
                        Vector3f next;
                        Point2f u = default;
                        u.x = r();
                        u.y = r();
                        if (layer.kind == FlakeKind.HenyeyGreenstein)
                        {
                            pstd.optional<PhaseFunctionSample> scatteredTo = new HGPhaseFunction(layer.g).Sample_p(-w, u);
                            if (!scatteredTo || scatteredTo.value().pdf == 0)
                            {
                                break;
                            }
                            next = scatteredTo.value().wi;
                        }
                        else
                        {
                            Vector3f m = layer.SampleNormal(-w, u);
                            next = Reflect(-w, m);
                        }
                        // (the direction has the density of the phase function: the weight is what the scattering keeps)
                        beta *= layer.Kept(-w, next);
                        if (!(bool)beta)
                        {
                            break;
                        }
                        if (keep)
                        {
                            chain = PolarisedFacet.Compose(chain, hasChain, FlakeMatrix(bsdf, side, layer, -w, next, fromCamera), fromCamera);
                            hasChain = true;
                        }
                        w = next;
                        continue;
                    }
                    // The next layer, the substrate, or the outside
                    k += down ? 1 : -1;
                    if (k < 0)
                    {
                        break;
                    }
                    if (k == count)
                    {
                        if (substrate == null)
                        {
                            break;
                        }
                        k = count - 1;
                        depth = layers[k].thickness;
                        ++events;
                        if (events >= 2)
                        {
                            Float through = 1;
                            for (int j = 0; j < count; ++j)
                            {
                                through *= Across(j, wi);
                            }
                            SampledSpectrum joined = beta * substrate.f(-w, wi, mode) * through;
                            if ((bool)joined)
                            {
                                more += joined;
                                if (keep)
                                {
                                    StackBxDF.Accumulate(ref matrices, ref any,
                                                         PolarisedFacet.Compose(chain, hasChain, StackBxDF.EvaluatedMatrix(bsdf, side, substrate, -w, wi, mode),
                                                                                fromCamera),
                                                         joined, directionExit, directionEnter);
                                }
                            }
                        }
                        Float uc = r();
                        Point2f u = default;
                        u.x = r();
                        u.y = r();
                        pstd.optional<BSDFSample> bs = substrate.Sample_f(-w, uc, u, mode, BxDFReflTransFlags.Reflection);
                        if (!bs || !(bool)bs.value().f || bs.value().pdf == 0 || !(bs.value().wi.z > 0))
                        {
                            break;
                        }
                        beta *= bs.value().f * AbsCosTheta(bs.value().wi) / bs.value().pdf;
                        if (keep)
                        {
                            chain = PolarisedFacet.Compose(chain, hasChain,
                                                           StackBxDF.SampledMatrix(bsdf, side, substrate, -w, bs.value().wi, uc, u, mode,
                                                                                   BxDFReflTransFlags.Reflection, fromCamera, false),
                                                           fromCamera);
                            hasChain = true;
                        }
                        w = bs.value().wi;
                        continue;
                    }
                    depth = down ? 0 : layers[k].thickness;
                }
            }
            return sum + more / nSamples;
        }

        public override pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if (polarisedInside)
            {
                return Inside(wo, uc, u, mode, sampleFlags);
            }
            return Sample(default, wo, uc, u, mode, sampleFlags, false, out bool _, out Attenuation _);
        }

        public override pstd.optional<BSDFSample> Sample_f(BSDF bsdf, Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags,
                                                           out bool polarised, out Attenuation matrix)
        {
            return Sample(bsdf, wo, uc, u, mode, sampleFlags, true, out polarised, out matrix);
        }

        // The walk itself: its direction when it leaves the layers, with what it carries. With `single` the walk ends at its
        // first event, and what the layers absorb on the way out is in its weight.
        private pstd.optional<BSDFSample> Sample(BSDF bsdf, Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags, bool keep,
                                                 out bool polarised, out Attenuation matrix)
        {
            polarised = false;
            matrix = default;
            bool flip = false;
            if (substrate != null && wo.z < 0)
            {
                wo = -wo;
                flip = true;
            }
            if (wo.z == 0)
            {
                return default;
            }
            Float side = flip ? -1 : 1;
            bool fromCamera = mode == TransportMode.Radiance;
            int count = layers.Length;
            bool fromTop = wo.z > 0;
            RNG rng = new RNG(Hash(GetOptions().seed, wo), Hash(uc, u));
            Float r() { return std.min<Float>(rng.Uniform<Float>(), OneMinusEpsilon); }
            Vector3f w = -wo;
            int k = fromTop ? 0 : count - 1;
            Float depth = fromTop ? 0 : layers[k].thickness;
            SampledSpectrum beta = new SampledSpectrum(1f);
            Attenuation chain = default;
            bool hasChain = false;
            int events = 0;
            // (every event so far was the reflection of a substrate that is a mirror)
            bool mirrored = true;
            for (int step = 0; step < maxDepth; ++step)
            {
                if (keep && hasChain)
                {
                    StackBxDF.Rebalance(ref beta, ref chain);
                }
                if (events > 3 && beta.MaxComponentValue() < 0.25f)
                {
                    Float q = std.max<Float>(0, 1 - beta.MaxComponentValue());
                    if (r() < q)
                    {
                        return default;
                    }
                    beta /= 1 - q;
                }
                if (w.z == 0)
                {
                    return default;
                }
                FlakeLayer layer = layers[k];
                bool down = w.z < 0;
                Float flight = SampleExponential(r(), layer.Sigma(w) / AbsCosTheta(w));
                bool left = false;
                if (flight < (down ? layer.thickness - depth : depth))
                {
                    depth += down ? flight : -flight;
                    ++events;
                    mirrored = false;
                    Vector3f next;
                    Point2f u2 = default;
                    u2.x = r();
                    u2.y = r();
                    if (layer.kind == FlakeKind.HenyeyGreenstein)
                    {
                        pstd.optional<PhaseFunctionSample> scatteredTo = new HGPhaseFunction(layer.g).Sample_p(-w, u2);
                        if (!scatteredTo || scatteredTo.value().pdf == 0)
                        {
                            return default;
                        }
                        next = scatteredTo.value().wi;
                    }
                    else
                    {
                        next = Reflect(-w, layer.SampleNormal(-w, u2));
                    }
                    beta *= layer.Kept(-w, next);
                    if (keep)
                    {
                        chain = PolarisedFacet.Compose(chain, hasChain, FlakeMatrix(bsdf, side, layer, -w, next, fromCamera), fromCamera);
                        hasChain = true;
                    }
                    w = next;
                    if (single)
                    {
                        if (w.z == 0)
                        {
                            return default;
                        }
                        beta *= Leaves(k, depth, w);
                        left = true;
                    }
                }
                else
                {
                    k += down ? 1 : -1;
                    if (k == count && substrate != null)
                    {
                        k = count - 1;
                        depth = layers[k].thickness;
                        ++events;
                        Float uc2 = r();
                        Point2f u2 = default;
                        u2.x = r();
                        u2.y = r();
                        pstd.optional<BSDFSample> bs = substrate.Sample_f(-w, uc2, u2, mode, BxDFReflTransFlags.Reflection);
                        if (!bs || !(bool)bs.value().f || bs.value().pdf == 0 || !(bs.value().wi.z > 0))
                        {
                            return default;
                        }
                        beta *= bs.value().f * AbsCosTheta(bs.value().wi) / bs.value().pdf;
                        mirrored &= bs.value().IsSpecular();
                        if (keep)
                        {
                            chain = PolarisedFacet.Compose(chain, hasChain,
                                                           StackBxDF.SampledMatrix(bsdf, side, substrate, -w, bs.value().wi, uc2, u2, mode,
                                                                                   BxDFReflTransFlags.Reflection, fromCamera, false),
                                                           fromCamera);
                            hasChain = true;
                        }
                        w = bs.value().wi;
                        if (single)
                        {
                            beta *= Leaves(k, depth, w);
                            left = true;
                        }
                    }
                    else if (k < 0 || k == count)
                    {
                        left = true;
                    }
                    else
                    {
                        depth = down ? 0 : layers[k].thickness;
                    }
                }
                if (!left)
                {
                    continue;
                }
                if (!(bool)beta)
                {
                    return default;
                }
                if (events == 0)
                {
                    // No flake on the way: the light itself, in its own direction
                    if (!delta || (sampleFlags & BxDFReflTransFlags.Transmission) == 0)
                    {
                        return default;
                    }
                    return new BSDFSample(beta / AbsCosTheta(wo), flip ? wo : -wo, 1, BxDFFlags.SpecularTransmission, 1f, true);
                }
                bool reflection = SameHemisphere(wo, w);
                if ((sampleFlags & (reflection ? BxDFReflTransFlags.Reflection : BxDFReflTransFlags.Transmission)) == 0)
                {
                    return default;
                }
                SampledSpectrum value = beta / AbsCosTheta(w);
                polarised = keep && hasChain;
                matrix = chain;
                BxDFFlags kind = mirrored ? BxDFFlags.SpecularReflection : (reflection ? BxDFFlags.GlossyReflection : BxDFFlags.GlossyTransmission);
                return new BSDFSample(value, flip ? -w : w, 1, kind, 1f, true);
            }
            return default;
        }

        // The density that weights the samples: the paper's, the phase function of the layer where the light is first
        // scattered by the probability that it is that layer, and the substrate's by the probability of reaching it. With
        // more than one scattering a constant part is added: any function of the two directions weights the samples without
        // bias, and this one is never zero where the layers have a value.
        public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if (substrate != null && wo.z < 0)
            {
                wo = -wo;
                wi = -wi;
            }
            if (wo.z == 0 || wi.z == 0 || (substrate != null && wi.z < 0))
            {
                return 0;
            }
            bool reflection = SameHemisphere(wo, wi);
            if ((sampleFlags & (reflection ? BxDFReflTransFlags.Reflection : BxDFReflTransFlags.Transmission)) == 0)
            {
                return 0;
            }
            int count = layers.Length;
            bool fromTop = wo.z > 0;
            Float reaches = 1, density = 0;
            for (int step = 0; step < count; ++step)
            {
                int k = fromTop ? step : count - 1 - step;
                Float across = Across(k, wo);
                density += reaches * (1 - across) * layers[k].Phase(wo, wi);
                reaches *= across;
            }
            if (substrate != null)
            {
                density += reaches * substrate.PDF(wo, wi, mode, BxDFReflTransFlags.Reflection);
            }
            if (single)
            {
                return density;
            }
            Float constant = substrate != null ? AbsCosTheta(wi) * InvPi : Inv4Pi;
            return Lerp(0.75f, constant, density);
        }

        public override string ToString()
        {
            return StringPrintf("[ SpongeCakeBxDF layers: %d substrate: %s single: %s ]", layers.Length, substrate != null, single);
        }

        private FlakeLayer[] layers;
        private BxDF substrate;
        private bool single, delta;
        private int maxDepth, nSamples;
    }

    // SpongeCakeMaterial Definition
    // "spongecake": layers of microflakes, from the top.
    //
    //   Material "spongecake" "string layers" [ "fiber" "surface" ] "float roughness" [ 0.1 0.8 ] "float thickness" [ 1 5 ]
    //       "rgb albedo" [ 0.7 0.1 0.1  0.7 0.1 0.1 ] "texture angle1" "weave"
    //
    //   "string layers"          "surface" (flakes that lie in the surface), "fiber" (fibres) or "hg" (a Henyey-Greenstein
    //                            medium), one for each layer
    // For each layer, as a list with a value for each layer (one value: for all), as a texture for all the layers, or, with
    // the number of the layer after the name ("albedo2": the second from the top), as a value or a texture for that layer:
    //   "float roughness"        of the flakes (0.5): 1 is isotropic
    //   "float thickness"        the optical thickness, the thickness times the density of the flakes (1)
    //   "spectrum albedo"        (0.8)
    //   "spectrum f0"            the reflectance of the flakes at normal incidence (1)
    //   "float g"                the asymmetry of an "hg" layer (0)
    // The orientation of each layer, the normal of its flakes or the direction of its fibres, in the frame of the surface (x
    // along dpdu, z the normal). The last of these that is given:
    //   "vector3 orientation"    a list (default: the normal for flakes, x for fibres)
    //   "float angle", "float tilt"   in degrees, as the parameters above (a texture, "angle1", ...). Fibres: the direction
    //                            turned by `angle` from x about the normal, then lifted out of the surface by `tilt`. Flakes:
    //                            the normal leant by `tilt` towards the direction at `angle`.
    //   "string orientationmap"  a list of images, one for each layer ("" for none), read as normal maps are: the direction
    //                            is 2 rgb - 1
    //   "string base"            "none" (default: the layers let light through), "diffuse" with "spectrum reflectance" (0.5),
    //                            or "conductor" with "spectrum conductor.eta" and "conductor.k" (copper), or "reflectance"
    //                            in their place, and "float conductor.roughness" (0: a mirror), "bool remaproughness" (true),
    //                            or "dielectric": the layers on something that refracts, with "float eta" (1.5) and
    //                            "float base.roughness" (0)
    //   "bool delta"             with the light that crosses the layers without meeting a flake (true)
    //   "string scattering"      "multiple" (default) or "single", the closed form of the paper alone
    //   "integer maxdepth"       the events of a walk (1000000: the walks end by themselves, with roulette)
    //   "integer nsamples" (1)   the walks of an evaluation
    //   "bool polarisedinside"   (false) asked without polarisation, the walk still keeps the polarisation of the light
    //                            between its events and returns what unpolarised light gives
    public sealed unsafe class SpongeCakeMaterial : Material
    {
        public SpongeCakeMaterial(FlakeKind[] kinds, FloatTexture[] alpha, FloatTexture[] thickness, FloatTexture[] g, Vector3f[] axes, FloatTexture[] angle,
                                  FloatTexture[] tilt, Image[] orientationMap, SpectrumTexture[] albedo, SpectrumTexture[] f0, string baseKind,
                                  SpectrumTexture reflectance, bool hasReflectance, Spectrum conductorEta, Spectrum conductorK, FloatTexture conductorRoughness,
                                  bool remapRoughness, bool single, bool delta, int maxDepth, int nSamples, FloatTexture displacement, Image normalMap,
                                  Float baseEta)
        {
            this.baseEta = baseEta;
            this.kinds = kinds;
            this.alpha = alpha;
            this.thickness = thickness;
            this.g = g;
            this.axes = axes;
            this.angle = angle;
            this.tilt = tilt;
            this.orientationMap = orientationMap;
            this.albedo = albedo;
            this.f0 = f0;
            this.baseKind = baseKind;
            this.reflectance = reflectance;
            this.hasReflectance = hasReflectance;
            this.conductorEta = conductorEta;
            this.conductorK = conductorK;
            this.conductorRoughness = conductorRoughness;
            this.remapRoughness = remapRoughness;
            this.single = single;
            this.delta = delta;
            this.maxDepth = maxDepth;
            this.nSamples = nSamples;
            this.displacement = displacement;
            this.normalMap = normalMap;
        }

        public override bool CanEvaluateTextures<TextureEvaluator>(TextureEvaluator texEval)
        {
            List<FloatTexture> floats = new List<FloatTexture>();
            List<SpectrumTexture> spectra = new List<SpectrumTexture>();
            for (int i = 0; i < kinds.Length; ++i)
            {
                floats.Add(alpha[i]);
                floats.Add(thickness[i]);
                floats.Add(g[i]);
                floats.Add(angle[i]);
                floats.Add(tilt[i]);
                spectra.Add(albedo[i]);
                spectra.Add(f0[i]);
            }
            floats.Add(conductorRoughness);
            spectra.Add(reflectance);
            return texEval.CanEvaluate(floats.ToArray(), spectra.ToArray());
        }

        public override BSDF GetBSDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                       ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            FlakeLayer[] layers = new FlakeLayer[kinds.Length];
            for (int i = 0; i < layers.Length; ++i)
            {
                layers[i].kind = kinds[i];
                layers[i].alpha = Clamp(texEval.Apply(alpha[i], ctx), 1e-3f, 1);
                layers[i].thickness = std.max<Float>(0, texEval.Apply(thickness[i], ctx));
                layers[i].g = Clamp(texEval.Apply(g[i], ctx), -0.99f, 0.99f);
                layers[i].albedo = Clamp(texEval.Apply(albedo[i], ctx, lambda), 0, 1);
                // (as pbrt for a conductor given by its colour: the extinction of the conductor of index 1 that has this
                // reflectance at normal incidence)
                SampledSpectrum r = Clamp(texEval.Apply(f0[i], ctx, lambda), 0, 0.9999f);
                layers[i].flakeK = 2 * Sqrt(r) / Sqrt(ClampZero(new SampledSpectrum(1f) - r));

                // The orientation
                Vector3f axis = axes[i];
                if (angle[i] != null || tilt[i] != null)
                {
                    Float turn = angle[i] != null ? Radians(texEval.Apply(angle[i], ctx)) : 0;
                    Float lift = tilt[i] != null ? Radians(texEval.Apply(tilt[i], ctx)) : 0;
                    if (kinds[i] == FlakeKind.Fiber)
                    {
                        axis = new Vector3f(std.cos(lift) * std.cos(turn), std.cos(lift) * std.sin(turn), std.sin(lift));
                    }
                    else
                    {
                        axis = new Vector3f(std.sin(lift) * std.cos(turn), std.sin(lift) * std.sin(turn), std.cos(lift));
                    }
                }
                if (orientationMap[i] != null)
                {
                    WrapMode2D wrap = new WrapMode2D(WrapMode.Repeat);
                    Point2f uv = new Point2f(ctx.uv[0], 1 - ctx.uv[1]);
                    Vector3f mapped = new Vector3f(2 * orientationMap[i].BilerpChannel(uv, 0, wrap) - 1, 2 * orientationMap[i].BilerpChannel(uv, 1, wrap) - 1,
                                                   2 * orientationMap[i].BilerpChannel(uv, 2, wrap) - 1);
                    if (LengthSquared(mapped) > 0)
                    {
                        axis = Normalize(mapped);
                    }
                }
                layers[i].axis = axis;
            }
            BxDF substrate = null;
            if (baseKind == "diffuse")
            {
                substrate = new DiffuseBxDF(Clamp(texEval.Apply(reflectance, ctx, lambda), 0, 1));
            }
            else if (baseKind == "conductor")
            {
                Float roughness = texEval.Apply(conductorRoughness, ctx);
                if (remapRoughness)
                {
                    roughness = TrowbridgeReitzDistribution.RoughnessToAlpha(roughness);
                }
                SampledSpectrum index, extinction;
                if (hasReflectance)
                {
                    SampledSpectrum r = Clamp(texEval.Apply(reflectance, ctx, lambda), 0, 0.9999f);
                    index = new SampledSpectrum(1f);
                    extinction = 2 * Sqrt(r) / Sqrt(ClampZero(new SampledSpectrum(1f) - r));
                }
                else
                {
                    index = conductorEta.Sample(lambda);
                    extinction = conductorK.Sample(lambda);
                }
                substrate = new ConductorBxDF(new TrowbridgeReitzDistribution(roughness, roughness), index, extinction);
            }
            SpongeCakeBxDF cake = new SpongeCakeBxDF(layers, substrate, single, delta, maxDepth, nSamples);
            if (baseKind == "dielectric")
            {
                // The layers on something that refracts: the surface of the dielectric is an interface under them, and one walk
                // goes through the two (LayerStackBxDF, whose interfaces may be anything that has a value and samples)
                Float roughness = texEval.Apply(conductorRoughness, ctx);
                if (remapRoughness)
                {
                    roughness = TrowbridgeReitzDistribution.RoughnessToAlpha(roughness);
                }
                LayerMedium[] between = new LayerMedium[1];
                between[0].thickness = 1;
                between[0].albedo = new SampledSpectrum(0f);
                between[0].sigmaA = new SampledSpectrum(0f);
                BxDF[] interfaces = new BxDF[] { cake, new DielectricBxDF(baseEta, new TrowbridgeReitzDistribution(roughness, roughness)) };
                LayerStackBxDF stack = new LayerStackBxDF(interfaces, between, maxDepth, nSamples, false);
                stack.polarisedInside = polarisedInside;
                return new BSDF(ctx.n, ctx.ns, ctx.dpdus, buf.Alloc(stack));
            }
            cake.polarisedInside = polarisedInside;
            return new BSDF(ctx.n, ctx.ns, ctx.dpdus, buf.Alloc(cake));
        }

        public static string Name() { return "SpongeCakeMaterial"; }

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
            return pbrt.StringPrintf("[ SpongeCakeMaterial layers: %d base: %s ]", kinds.Length, baseKind);
        }

        // A number for each layer: "name<layer>" for the layer (a value or a texture), else the list "name" (one value: for
        // all), else the texture "name" for all, else `fallback` (null with `optional`: nothing given)
        internal static FloatTexture[] Floats(TextureParameterDictionary parameters, string name, int count, Float fallback, bool optional, FileLoc loc,
                                              Allocator alloc)
        {
            List<Float> given = parameters.GetFloatArray(name);
            if (given.Count != 0 && given.Count != 1 && given.Count != count)
            {
                pbrt.ErrorExit(loc, pbrt.StringPrintf("\"%s\" needs %d values (or one for all).", name, count));
            }
            FloatTexture all = given.Count == 0 ? parameters.GetFloatTextureOrNull(name, alloc) : null;
            FloatTexture[] values = new FloatTexture[count];
            for (int i = 0; i < count; ++i)
            {
                FloatTexture own = parameters.GetFloatTextureOrNull(name + (i + 1), alloc);
                if (own != null)
                {
                    values[i] = own;
                }
                else if (given.Count != 0)
                {
                    values[i] = new FloatConstantTexture(given.Count == 1 ? given[0] : given[i]);
                }
                else if (all != null)
                {
                    values[i] = all;
                }
                else
                {
                    values[i] = optional ? null : new FloatConstantTexture(fallback);
                }
            }
            return values;
        }

        internal static SpectrumTexture[] Spectra(TextureParameterDictionary parameters, string name, int count, Float fallback, SpectrumType type,
                                                  FileLoc loc, Allocator alloc)
        {
            List<Spectrum> given = parameters.GetSpectrumArray(name, type, alloc);
            if (given.Count != 0 && given.Count != 1 && given.Count != count)
            {
                pbrt.ErrorExit(loc, pbrt.StringPrintf("\"%s\" needs %d values (or one for all).", name, count));
            }
            SpectrumTexture all = given.Count == 0 ? parameters.GetSpectrumTextureOrNull(name, type, alloc) : null;
            SpectrumTexture[] values = new SpectrumTexture[count];
            for (int i = 0; i < count; ++i)
            {
                SpectrumTexture own = parameters.GetSpectrumTextureOrNull(name + (i + 1), type, alloc);
                if (own != null)
                {
                    values[i] = own;
                }
                else if (given.Count != 0)
                {
                    values[i] = new SpectrumConstantTexture(given.Count == 1 ? given[0] : given[i]);
                }
                else if (all != null)
                {
                    values[i] = all;
                }
                else
                {
                    values[i] = new SpectrumConstantTexture(new ConstantSpectrum(fallback));
                }
            }
            return values;
        }

        public static SpongeCakeMaterial Create(TextureParameterDictionary parameters, Image normalMap, FileLoc loc, Allocator alloc)
        {
            List<string> names = parameters.GetStringArray("layers");
            int count = names.Count;
            if (count == 0)
            {
                pbrt.ErrorExit(loc, "\"spongecake\" needs \"string layers\": \"surface\", \"fiber\" or \"hg\" for each layer.");
            }
            FlakeKind[] kinds = new FlakeKind[count];
            for (int i = 0; i < count; ++i)
            {
                if (names[i] == "surface")
                {
                    kinds[i] = FlakeKind.Surface;
                }
                else if (names[i] == "fiber")
                {
                    kinds[i] = FlakeKind.Fiber;
                }
                else if (names[i] == "hg")
                {
                    kinds[i] = FlakeKind.HenyeyGreenstein;
                }
                else
                {
                    pbrt.ErrorExit(loc, pbrt.StringPrintf("%s: a layer is \"surface\", \"fiber\" or \"hg\".", names[i]));
                }
            }
            List<Vector3f> given = parameters.GetVector3fArray("orientation");
            if (given.Count != 0 && given.Count != 1 && given.Count != count)
            {
                pbrt.ErrorExit(loc, pbrt.StringPrintf("\"orientation\" needs %d vectors (or one for all).", count));
            }
            List<string> maps = parameters.GetStringArray("orientationmap");
            if (maps.Count != 0 && maps.Count != count)
            {
                pbrt.ErrorExit(loc, pbrt.StringPrintf("\"orientationmap\" needs %d names (\"\" for a layer without one).", count));
            }
            Vector3f[] axes = new Vector3f[count];
            Image[] orientationMap = new Image[count];
            for (int i = 0; i < count; ++i)
            {
                Vector3f axis = kinds[i] == FlakeKind.Fiber ? new Vector3f(1, 0, 0) : new Vector3f(0, 0, 1);
                if (given.Count != 0)
                {
                    axis = given.Count == 1 ? given[0] : given[i];
                    if (pbrt.LengthSquared(axis) == 0)
                    {
                        pbrt.ErrorExit(loc, "\"orientation\": a vector of length 0.");
                    }
                }
                axes[i] = pbrt.Normalize(axis);
                if (maps.Count != 0 && maps[i].Length != 0)
                {
                    string filename = pbrt.ResolveFilename(maps[i]);
                    Image image = Image.Read(filename, new Allocator(), ColorEncoding.Linear).image;
                    ImageChannelDesc rgb = image.GetChannelDesc(new string[] { "R", "G", "B" });
                    if (!rgb)
                    {
                        pbrt.ErrorExit(loc, pbrt.StringPrintf("%s: an orientation map needs the channels R, G and B.", filename));
                    }
                    orientationMap[i] = image.SelectChannels(rgb);
                }
            }
            string baseKind = parameters.GetOneString("base", "none");
            if (baseKind != "none" && baseKind != "diffuse" && baseKind != "conductor" && baseKind != "dielectric")
            {
                pbrt.ErrorExit(loc, pbrt.StringPrintf("%s: \"base\" is \"none\", \"diffuse\", \"conductor\" or \"dielectric\".", baseKind));
            }
            string scattering = parameters.GetOneString("scattering", "multiple");
            if (scattering != "multiple" && scattering != "single")
            {
                pbrt.ErrorExit(loc, pbrt.StringPrintf("%s: \"scattering\" is \"multiple\" or \"single\".", scattering));
            }
            SpectrumTexture reflectance = parameters.GetSpectrumTextureOrNull("reflectance", SpectrumType.Albedo, alloc);
            bool hasReflectance = reflectance != null;
            if (reflectance == null)
            {
                reflectance = new SpectrumConstantTexture(new ConstantSpectrum(0.5f));
            }
            // (the roughness of a conductor or dielectric base)
            FloatTexture conductorRoughness = parameters.GetFloatTextureOrNull(baseKind == "dielectric" ? "base.roughness" : "conductor.roughness", alloc);
            if (conductorRoughness == null)
            {
                conductorRoughness = new FloatConstantTexture(0);
            }
            SpongeCakeMaterial material = new SpongeCakeMaterial(kinds, Floats(parameters, "roughness", count, 0.5f, false, loc, alloc),
                                          Floats(parameters, "thickness", count, 1, false, loc, alloc), Floats(parameters, "g", count, 0, false, loc, alloc),
                                          axes, Floats(parameters, "angle", count, 0, true, loc, alloc), Floats(parameters, "tilt", count, 0, true, loc, alloc),
                                          orientationMap, Spectra(parameters, "albedo", count, 0.8f, SpectrumType.Albedo, loc, alloc),
                                          Spectra(parameters, "f0", count, 1, SpectrumType.Albedo, loc, alloc),
                                          baseKind, reflectance, hasReflectance,
                                          parameters.GetOneSpectrum("conductor.eta", pbrt.GetNamedSpectrum("metal-Cu-eta"), SpectrumType.Unbounded, alloc),
                                          parameters.GetOneSpectrum("conductor.k", pbrt.GetNamedSpectrum("metal-Cu-k"), SpectrumType.Unbounded, alloc),
                                          conductorRoughness, parameters.GetOneBool("remaproughness", true), scattering == "single",
                                          parameters.GetOneBool("delta", true), parameters.GetOneInt("maxdepth", 1000000),
                                          parameters.GetOneInt("nsamples", 1), parameters.GetFloatTextureOrNull("displacement", alloc), normalMap,
                                          parameters.GetOneFloat("eta", 1.5f));
            material.polarisedInside = parameters.GetOneBool("polarisedinside", false);
            return material;
        }

        private bool polarisedInside;

        private Float baseEta;
        private FlakeKind[] kinds;
        private FloatTexture[] alpha, thickness, g, angle, tilt;
        private Vector3f[] axes;
        private Image[] orientationMap;
        private SpectrumTexture[] albedo, f0;
        private string baseKind;
        private SpectrumTexture reflectance;
        private bool hasReflectance;
        private Spectrum conductorEta, conductorK;
        private FloatTexture conductorRoughness;
        private bool remapRoughness, single, delta;
        private int maxDepth, nSamples;
        private FloatTexture displacement;
        private Image normalMap;
    }
}
