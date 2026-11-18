// (not in pbrt-v4) Layered materials of any number of layers, as one flat stack.
//
// pbrt's LayeredBxDF has two interfaces and one medium. More layers are made by putting a layered BxDF inside another one,
// and then every evaluation of the outer one evaluates and samples the inner one, which is itself a random walk, and asks
// for its density, which is another. LayerStackBxDF has all the interfaces and all the media side by side instead, and
// one walk goes through all of them:
//
//   interface 0 | medium 0 | interface 1 | medium 1 | ... | interface n-1
//
// The value is estimated as in Gamboa, Gruson and Nowrouzezahrai, "An Efficient Transport Estimator for Complex Layered
// Materials" (Eurographics 2020): one walk from wo, without any bidirectional construction, and, at each of its vertices,
// a connection to wi that crosses however many interfaces lie between the vertex and the outside. The directions of that
// connection are sampled once for each walk, from wi inwards, through one interface after another (the "chain"): a vertex
// in the medium m, or on the interface at the far side of it, is joined to the direction that the chain has in m. Nothing
// is weighted against anything else: each path is made in one way only.
//
// The one way. The paper joins a path where it last turns towards the outside, and its chain only goes through interfaces;
// that loses every path that turns at a mirror, which has no value to join with. Here the chain is turned back by the
// interfaces that reflect it as mirrors as well as let through by the others, and ends at the first reflection with a
// value. A path is then joined where it is last reflected with a value or scattered in a medium (what follows is what the
// chain is made of); and a path that never is, at the first interface that lets it through with a value, which the walk
// reaches by specular directions only (the flag `pure`).
//
// The density that the integrator weights the samples with is not estimated by a walk. It is a closed form: the lobes of
// Randrianandrasana, Callet and Lucas, "Transfer matrix based layered materials rendering" (SIGGRAPH 2021), the two-flux
// model of its section 5. Each interface and each medium has a 2x2 transfer matrix for the energy and one for the energy
// times the asymmetry of a Henyey-Greenstein lobe; their products give, for each interface, the energy and the width of the
// lobe of the light that went down to it and came back, with all the reflections between the layers. The paper renders
// with those lobes, which is approximate; here they only weight unbiased estimates against each other, and for that any
// function of the two directions does (the weights of the two ways of sampling a direction add up to one whatever the
// function is). So the model's tables (the albedo of a rough interface, total internal reflection) are replaced by the
// Fresnel reflectance at the mean direction, and a scattering medium gives its light to a diffuse lobe.
//
// Polarisation: as in the renderer's other layered BxDF, every scattering of a path has its Mueller matrix (the Fresnel
// matrix of a dielectric or a conductor at the facet that sends one direction to the other; a diffuse base and a scattering
// in a medium depolarise), the matrices are composed along the walk and along the chain, and the matrix of the value is the
// sum of the matrices of the paths over the sum of their scalar contributions.

using System;
using System.Collections.Generic;
using Attenuation = pbrt.PolarisedLightSystem.Attenuation;

namespace pbrt
{
    // The medium between two interfaces. Lengths are in the unit of `thickness`.
    public struct LayerMedium
    {
        public Float thickness;
        // The scattering coefficient (one number: the walk samples its free flights with it), the colour that a scattering
        // gives the light, the asymmetry of the phase function, and the absorption coefficient
        public Float sigmaS;
        public SampledSpectrum albedo;
        public Float g;
        public SampledSpectrum sigmaA;
    }

    // LayerStackBxDF Definition
    public sealed unsafe class LayerStackBxDF : BxDF
    {
        public LayerStackBxDF() { }

        // interfaces[0] is the top. twoSided: the same stack from below (an opaque base).
        public LayerStackBxDF(BxDF[] interfaces, LayerMedium[] media, int maxDepth, int nSamples, bool twoSided)
        {
            this.interfaces = interfaces;
            this.media = media;
            this.maxDepth = maxDepth;
            this.nSamples = nSamples;
            this.twoSided = twoSided;
        }

        public override void Regularize()
        {
            for (int i = 0; i < interfaces.Length; ++i)
            {
                interfaces[i].Regularize();
            }
        }

        public override BxDFFlags Flags()
        {
            bool diffuse = false, glossy = false, transmissive = true;
            for (int i = 0; i < interfaces.Length; ++i)
            {
                BxDFFlags flags = interfaces[i].Flags();
                diffuse |= IsDiffuse(flags);
                glossy |= IsGlossy(flags);
                transmissive &= IsTransmissive(flags);
            }
            for (int i = 0; i < media.Length; ++i)
            {
                diffuse |= media[i].sigmaS > 0;
            }
            BxDFFlags result = BxDFFlags.Reflection;
            if (IsSpecular(interfaces[0].Flags()))
            {
                result = result | BxDFFlags.Specular;
            }
            if (diffuse)
            {
                result = result | BxDFFlags.Diffuse;
            }
            else if (glossy)
            {
                result = result | BxDFFlags.Glossy;
            }
            if (transmissive)
            {
                result = result | BxDFFlags.Transmission;
            }
            return result;
        }

        // What is left of the light after the vertical distance `span` of the medium along w
        // An interface whose samples come with their density, which PDF() returns for any pair of directions (the others walk,
        // and their density is only proportional): two ways of sampling a scattering on one can be weighed against each other
        private static bool Exact(BxDF iface)
        {
            while (iface is AnyBxDF any)
            {
                iface = any.inner;
            }
            return iface is DielectricBxDF || iface is ConductorBxDF || iface is DiffuseBxDF;
        }

        // A path whose last events are scatterings with a value on interfaces that have densities (and reflections by mirrors
        // between them) can be made in several ways: the walk samples the first of these events, the chain the rest, and the
        // event between the two is evaluated. This is the weight of one of them against the others (the power heuristic).
        // The event that is evaluated has the densities `forward` (as the walk would sample it) and `reverse` (as the chain
        // would); before it the walk sampled `runCount` events, after it the chain sampled the links lastLink ... 0.
        private static Float Strategy(Float* runForward, Float* runReverse, int runCount, Float forward, Float reverse, int lastLink, Float* linkForward,
                                      Float* linkReverse, bool* linkSpecular)
        {
            double sum = 1, ratio = 1, next = reverse;
            for (int r = runCount - 1; r >= 0; --r)
            {
                // (the way that evaluates the walk's event r: the chain samples the events after it)
                ratio *= next / runForward[r];
                sum += ratio * ratio;
                next = runReverse[r];
            }
            ratio = 1;
            double previous = forward;
            for (int i = lastLink; i >= 0; --i)
            {
                if (linkSpecular[i])
                {
                    continue;
                }
                // (the way that evaluates the chain's link i: the walk samples the events before it)
                ratio *= previous / linkReverse[i];
                sum += ratio * ratio;
                previous = linkForward[i];
            }
            return double.IsNaN(sum) ? 0 : (Float)(1 / sum);
        }

        private static SampledSpectrum Through(LayerMedium medium, Float span, Vector3f w)
        {
            Float length = span / AbsCosTheta(w);
            return Exp(-(medium.sigmaA + new SampledSpectrum(medium.sigmaS)) * length);
        }

        public override bool HasMuellerMatrix() { return true; }

        // As a layer of another BxDF, and for its own layers (LayeredBxDF.carried, carriedPolarised): what the path carries at
        // each wavelength and, with polarisation, the frame of the surface and the matrix of the path that led here. A mirror
        // among the interfaces (BraggLayerBxDF) chooses between reflection and transmission by them.
        public SampledSpectrum carried;
        public bool carriedPolarised;
        public BSDF carriedFrame;
        public Attenuation carriedChain;
        public bool carriedHasChain;

        private bool tracking, trackingFromCamera;
        private BSDF trackingFrame;

        // At the start of an evaluation
        private void Context(bool polarised, BSDF bsdf, Float side, bool fromCamera)
        {
            tracking = polarised;
            trackingFromCamera = fromCamera;
            trackingFrame = polarised ? (side < 0 ? bsdf.TurnedOver() : bsdf) : default;
            for (int i = 0; i < interfaces.Length; ++i)
            {
                Hand(interfaces[i], new SampledSpectrum(1f), default, false);
            }
        }

        // Before an interface is sampled: what the walk carries and, with polarisation, its matrix so far
        private void Hand(BxDF iface, SampledSpectrum weight, Attenuation chain, bool hasChain)
        {
            while (iface is AnyBxDF any)
            {
                iface = any.inner;
            }
            if (!(iface is BraggLayerBxDF) && !(iface is LayerStackBxDF) && !(iface is LayeredBxDF<AnyBxDF, AnyBxDF>))
            {
                return;
            }
            if ((bool)carried)
            {
                weight *= carried;
            }
            bool has = tracking && hasChain;
            if (tracking && carriedPolarised && carriedHasChain)
            {
                chain = hasChain ? PolarisedFacet.Compose(carriedChain, true, chain, trackingFromCamera) : carriedChain;
                has = true;
            }
            if (iface is BraggLayerBxDF mirror)
            {
                mirror.carried = weight;
                mirror.carriedPolarised = tracking;
                mirror.carriedFrame = trackingFrame;
                mirror.carriedChain = chain;
                mirror.carriedHasChain = has;
            }
            else if (iface is LayerStackBxDF flat)
            {
                flat.carried = weight;
                flat.carriedPolarised = tracking;
                flat.carriedFrame = trackingFrame;
                flat.carriedChain = chain;
                flat.carriedHasChain = has;
            }
            else if (iface is LayeredBxDF<AnyBxDF, AnyBxDF> stack)
            {
                stack.carried = weight;
                stack.carriedPolarised = tracking;
                stack.carriedFrame = trackingFrame;
                stack.carriedChain = chain;
                stack.carriedHasChain = has;
            }
        }

        // The value of an interface and, with `keep`, the matrix of the scattering (a first element of 1). An interface that is a
        // walk of its own gives both at once.
        private static SampledSpectrum Value(BxDF iface, Vector3f a, Vector3f b, TransportMode mode, bool keep, BSDF bsdf, Float side, out Attenuation matrix)
        {
            matrix = default;
            if (!keep)
            {
                return iface.f(a, b, mode);
            }
            if (StackBxDF.Walked(iface))
            {
                SampledSpectrum both = iface.f(side < 0 ? bsdf.TurnedOver() : bsdf, a, b, mode, out bool polarised, out matrix);
                if (!polarised)
                {
                    matrix = Attenuation.Lambert(new SampledSpectrum(1f));
                }
                return both;
            }
            SampledSpectrum value = iface.f(a, b, mode);
            if ((bool)value)
            {
                matrix = StackBxDF.EventMatrix(bsdf, side, iface, a, b, mode == TransportMode.Radiance);
            }
            return value;
        }

        // A sample of an interface and, with `keep`, its matrix. `against`: sampled from the far end of the scattering (the
        // chain, with the other mode): the walk goes from the sampled direction to a.
        private static pstd.optional<BSDFSample> Draw(BxDF iface, Vector3f a, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags flags, bool keep,
                                                      BSDF bsdf, Float side, bool fromCamera, bool against, out Attenuation matrix)
        {
            matrix = default;
            if (!keep)
            {
                return iface.Sample_f(a, uc, u, mode, flags);
            }
            if (StackBxDF.Walked(iface))
            {
                pstd.optional<BSDFSample> both = iface.Sample_f(side < 0 ? bsdf.TurnedOver() : bsdf, a, uc, u, mode, flags, out bool polarised, out matrix);
                if (!polarised)
                {
                    matrix = Attenuation.Lambert(new SampledSpectrum(1f));
                }
                return both;
            }
            pstd.optional<BSDFSample> bs = iface.Sample_f(a, uc, u, mode, flags);
            if ((bool)bs)
            {
                matrix = against ? StackBxDF.EventMatrix(bsdf, side, iface, bs.value().wi, a, fromCamera)
                                 : StackBxDF.EventMatrix(bsdf, side, iface, a, bs.value().wi, fromCamera);
            }
            return bs;
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
            if (polarisedInside && !carriedPolarised)
            {
                return Inside(wo, wi, mode);
            }
            // (as a layer of a BxDF that is evaluated with polarisation: the walk that its matrix will be asked of)
            return Evaluate(wo, wi, mode, carriedFrame, carriedPolarised, out Attenuation _, out bool _);
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

        // The estimate of the value and, with `keep`, the sum of the matrices of its paths, each times its scalar contribution
        // (`any`: there is one)
        private SampledSpectrum Evaluate(Vector3f wo, Vector3f wi, TransportMode mode, BSDF bsdf, bool keep, out Attenuation matrices, out bool any)
        {
            matrices = default;
            any = false;
            Float side = 1;
            if (twoSided && wo.z < 0)
            {
                wo = -wo;
                wi = -wi;
                side = -1;
            }
            if (wo.z == 0 || wi.z == 0)
            {
                return new SampledSpectrum(0f);
            }
            bool fromCamera = mode == TransportMode.Radiance;
            Context(keep, bsdf, side, fromCamera);
            Vector3f directionExit = default, directionEnter = default;
            if (keep)
            {
                Vector3f woRender = bsdf.LocalToRender(side * wo), wiRender = bsdf.LocalToRender(side * wi);
                directionExit = fromCamera ? woRender : wiRender;
                directionEnter = fromCamera ? -wiRender : -woRender;
            }
            int count = interfaces.Length;
            bool enterTop = wo.z > 0, exitTop = wi.z > 0;
            int enter = enterTop ? 0 : count - 1, exit = exitTop ? 0 : count - 1;
            SampledSpectrum sum = new SampledSpectrum(0f);
            if (enter == exit)
            {
                sum = nSamples * Value(interfaces[enter], wo, wi, mode, keep, bsdf, side, out Attenuation direct);
                if (keep && (bool)sum)
                {
                    StackBxDF.Accumulate(ref matrices, ref any, direct, sum, directionExit, directionEnter);
                }
            }
            if (count == 1)
            {
                return sum / nSamples;
            }
            // (a stream of its own: an interface that walks, such as the layers of flakes, seeds its stream with the same directions)
            RNG rng = new RNG(Hash(GetOptions().seed, wo, 0x6c737461636bUL), Hash(wi));
            Float r() { return std.min<Float>(rng.Uniform<Float>(), OneMinusEpsilon); }

            // The chain: its segments, each in a medium, with the direction of the chain there (away from wi), its weight when it
            // enters the segment and, with polarisation, the matrix of the chain from the segment to wi, in the order of the walk
            const int most = 64;
            int* segmentMedium = stackalloc int[most];
            Vector3f* segmentDirection = stackalloc Vector3f[most];
            SampledSpectrum* segmentWeight = stackalloc SampledSpectrum[most];
            Attenuation[] segmentMatrix = keep ? new Attenuation[most] : null;
            // The ways of making a path that are weighed against each other (Strategy()): for the chain, the densities of its
            // links both ways, and whether everything from wi to a segment is such an event; for the walk, the same of its
            // events since the one that began its run (the interface it entered by, or a reflection with a value). Which paths
            // are weighed depends on the interfaces and on the path, not on which way made it.
            Float* linkForward = stackalloc Float[most];
            Float* linkReverse = stackalloc Float[most];
            bool* linkSpecular = stackalloc bool[most];
            bool* segmentClean = stackalloc bool[most];
            Float* runForward = stackalloc Float[most];
            Float* runReverse = stackalloc Float[most];
            for (int s = 0; s < nSamples; ++s)
            {
                // Sample the chain from wi: through the interfaces, and back from those that reflect as mirrors. It ends where an
                // interface reflects it with a value (a walk is joined there), or where it leaves the layers.
                int segments = 0;
                Vector3f from = wi;
                SampledSpectrum weight = new SampledSpectrum(1f);
                Attenuation outwards = default;
                bool hasOutwards = false;
                int at = exit;
                while (segments < most)
                {
                    Float uc = r();
                    Point2f u = default;
                    u.x = r();
                    u.y = r();
                    Hand(interfaces[at], new SampledSpectrum(1f), default, false);
                    pstd.optional<BSDFSample> link = Draw(interfaces[at], from, uc, u, !mode,
                                                          segments == 0 ? BxDFReflTransFlags.Transmission : BxDFReflTransFlags.All, keep, bsdf, side, fromCamera,
                                                          true, out Attenuation crossing);
                    if (!link || !(bool)link.value().f || link.value().pdf == 0 || link.value().wi.z == 0)
                    {
                        break;
                    }
                    if (link.value().IsReflection() && !link.value().IsSpecular())
                    {
                        break;
                    }
                    Vector3f direction = link.value().wi;
                    weight *= link.value().f * AbsCosTheta(direction) / link.value().pdf;
                    if (keep)
                    {
                        // (sampled from the side of wi, against the walk, which meets this interface before the ones nearer wi)
                        outwards = hasOutwards ? PolarisedFacet.Compose(crossing, true, outwards, fromCamera) : crossing;
                        hasOutwards = true;
                    }
                    // (the medium that the chain is in now: under the interface if it goes down, over it if it goes up)
                    int inside = direction.z < 0 ? at : at - 1;
                    if (inside < 0 || inside > count - 2)
                    {
                        break;
                    }
                    segmentMedium[segments] = inside;
                    bool mirrored = link.value().IsSpecular();
                    bool clean = Exact(interfaces[at]) && (mirrored ? link.value().IsReflection() : link.value().IsTransmission());
                    linkSpecular[segments] = mirrored;
                    linkReverse[segments] = link.value().pdf;
                    // (the walk meets the interface from this segment and leaves it towards where the chain came from)
                    // (no way has the walk sample the interface that the light leaves by: the first link)
                    linkForward[segments] = clean && !mirrored && segments > 0 ? interfaces[at].PDF(direction, from, mode, BxDFReflTransFlags.All) : 0;
                    segmentClean[segments] = clean && (segments == 0 || segmentClean[segments - 1]);
                    segmentDirection[segments] = direction;
                    segmentWeight[segments] = weight;
                    if (keep)
                    {
                        segmentMatrix[segments] = outwards;
                    }
                    ++segments;
                    weight *= Through(media[inside], media[inside].thickness, direction);
                    from = -direction;
                    at = direction.z < 0 ? inside + 1 : inside;
                }

                // The walk from wo. A path is joined to the chain once: where it is last reflected with a value or scattered in a
                // medium (the chain has no such event); and a path that never is, at the first interface that lets it through
                // with a value, which the walk reaches by specular directions only (`pure`). The first interface is the one
                // that the walk enters by.
                int m0 = enterTop ? 0 : count - 2;
                for (int g = 0; g < segments; ++g)
                {
                    if (segmentMedium[g] != m0 || (segmentDirection[g].z > 0) != enterTop)
                    {
                        continue;
                    }
                    SampledSpectrum across = Value(interfaces[enter], wo, -segmentDirection[g], mode, keep, bsdf, side, out Attenuation first);
                    if ((bool)across)
                    {
                        across *= segmentWeight[g] * Through(media[m0], media[m0].thickness, segmentDirection[g]);
                        if (segmentClean[g] && Exact(interfaces[enter]))
                        {
                            across *= Strategy(runForward, runReverse, 0,
                                               interfaces[enter].PDF(wo, -segmentDirection[g], mode, BxDFReflTransFlags.Transmission), 0, g, linkForward,
                                               linkReverse, linkSpecular);
                        }
                        sum += across;
                        if (keep)
                        {
                            StackBxDF.Accumulate(ref matrices, ref any, PolarisedFacet.Compose(first, true, segmentMatrix[g], fromCamera), across,
                                                 directionExit, directionEnter);
                        }
                    }
                }
                Float uc0 = r();
                Point2f u0 = default;
                u0.x = r();
                u0.y = r();
                Hand(interfaces[enter], new SampledSpectrum(1f), default, false);
                pstd.optional<BSDFSample> entered = Draw(interfaces[enter], wo, uc0, u0, mode, BxDFReflTransFlags.Transmission, keep, bsdf, side, fromCamera,
                                                         false, out Attenuation chain);
                if (!entered || !(bool)entered.value().f || entered.value().pdf == 0 || entered.value().wi.z == 0)
                {
                    continue;
                }
                SampledSpectrum beta = entered.value().f * AbsCosTheta(entered.value().wi) / entered.value().pdf;
                Vector3f w = entered.value().wi;
                bool pure = entered.value().IsSpecular();
                bool run = !pure && Exact(interfaces[enter]);
                int runCount = 1;
                runForward[0] = entered.value().pdf;
                runReverse[0] = 0;
                // (`chain` is the matrix of the walk so far, each scattering with a first element of 1: beta has the scalar factors)
                int medium = m0;
                // (the depth below the upper interface of the medium)
                Float depth = enterTop ? 0 : media[medium].thickness;
                for (int step = 0; step < maxDepth; ++step)
                {
                    if (keep)
                    {
                        StackBxDF.Rebalance(ref beta, ref chain);
                    }
                    if (step > 3 && beta.MaxComponentValue() < 0.25f)
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
                    LayerMedium current = media[medium];
                    bool up = w.z > 0;
                    Float toBoundary = up ? depth : current.thickness - depth;
                    if (current.sigmaS > 0)
                    {
                        Float flight = SampleExponential(r(), current.sigmaS / AbsCosTheta(w));
                        if (flight < toBoundary)
                        {
                            // A scattering in the medium, joined to every segment of the chain in it
                            depth += up ? -flight : flight;
                            beta *= Exp(-current.sigmaA * (flight / AbsCosTheta(w))) * current.albedo;
                            HGPhaseFunction phase = new HGPhaseFunction(current.g);
                            for (int g = 0; g < segments; ++g)
                            {
                                if (segmentMedium[g] != medium)
                                {
                                    continue;
                                }
                                // (the weight of the chain has the cosine of its direction, which belongs to a vertex on an
                                // interface: a vertex in the medium is a point of the ray of the chain, whose length for a
                                // depth is the depth over that cosine)
                                Vector3f d = segmentDirection[g];
                                Float span = d.z < 0 ? depth : current.thickness - depth;
                                SampledSpectrum scatteredTo = beta * phase.p(-w, -d) * segmentWeight[g] * Through(current, span, d) / AbsCosTheta(d);
                                sum += scatteredTo;
                                if (keep && (bool)scatteredTo)
                                {
                                    // (a scattering in the medium depolarises)
                                    Attenuation path = PolarisedFacet.Compose(chain, true, Attenuation.Lambert(new SampledSpectrum(1f)), fromCamera);
                                    StackBxDF.Accumulate(ref matrices, ref any, PolarisedFacet.Compose(path, true, segmentMatrix[g], fromCamera),
                                                         scatteredTo, directionExit, directionEnter);
                                }
                            }
                            Point2f up2 = default;
                            up2.x = r();
                            up2.y = r();
                            pstd.optional<PhaseFunctionSample> scattered = phase.Sample_p(-w, up2);
                            if (!scattered || scattered.value().pdf == 0 || scattered.value().wi.z == 0)
                            {
                                break;
                            }
                            beta *= scattered.value().p / scattered.value().pdf;
                            w = scattered.value().wi;
                            pure = false;
                            run = false;
                            if (keep)
                            {
                                chain = PolarisedFacet.Compose(chain, true, Attenuation.Lambert(new SampledSpectrum(1f)), fromCamera);
                            }
                            continue;
                        }
                    }
                    if ((bool)current.sigmaA)
                    {
                        beta *= Exp(-current.sigmaA * (toBoundary / AbsCosTheta(w)));
                    }

                    // The interface that the walk reaches
                    int j = up ? medium : medium + 1;
                    BxDF iface = interfaces[j];
                    // What the interface reflects into the segments of the chain that end on it, on this side
                    for (int g = 0; g < segments; ++g)
                    {
                        if (segmentMedium[g] != medium || (segmentDirection[g].z > 0) != up)
                        {
                            continue;
                        }
                        SampledSpectrum reflected = Value(iface, -w, -segmentDirection[g], mode, keep, bsdf, side, out Attenuation turned);
                        if ((bool)reflected)
                        {
                            reflected *= beta * segmentWeight[g] * Through(current, current.thickness, segmentDirection[g]);
                            if (segmentClean[g] && Exact(iface))
                            {
                                // (the first of the ways: the reflection begins a run of the walk)
                                reflected *= Strategy(runForward, runReverse, 0, iface.PDF(-w, -segmentDirection[g], mode, BxDFReflTransFlags.All), 0, g,
                                                      linkForward, linkReverse, linkSpecular);
                            }
                            sum += reflected;
                            if (keep)
                            {
                                Attenuation path = PolarisedFacet.Compose(chain, true, turned, fromCamera);
                                StackBxDF.Accumulate(ref matrices, ref any, PolarisedFacet.Compose(path, true, segmentMatrix[g], fromCamera), reflected,
                                                     directionExit, directionEnter);
                            }
                        }
                    }
                    // (during a run of the walk: one of the ways of making the path, with its weight)
                    bool weighed = run && Exact(iface) && !IsSpecular(iface.Flags());
                    if (pure || weighed)
                    {
                        // Nothing with a value yet: what the interface lets through, to wi itself if it is the last one on that
                        // side, to the segments of the chain that end on its other side otherwise
                        if (j == (up ? 0 : count - 1))
                        {
                            if (j == exit)
                            {
                                SampledSpectrum left = beta * Value(iface, -w, wi, mode, keep, bsdf, side, out Attenuation last);
                                if (weighed && (bool)left)
                                {
                                    left *= Strategy(runForward, runReverse, runCount, 0, iface.PDF(wi, -w, !mode, BxDFReflTransFlags.Transmission), -1,
                                                     linkForward, linkReverse, linkSpecular);
                                }
                                sum += left;
                                if (keep && (bool)left)
                                {
                                    StackBxDF.Accumulate(ref matrices, ref any, PolarisedFacet.Compose(chain, true, last, fromCamera), left, directionExit,
                                                         directionEnter);
                                }
                            }
                        }
                        else
                        {
                            int next = up ? medium - 1 : medium + 1;
                            for (int g = 0; g < segments; ++g)
                            {
                                if (segmentMedium[g] != next || (segmentDirection[g].z > 0) == up || (weighed && !segmentClean[g]))
                                {
                                    continue;
                                }
                                SampledSpectrum passed = Value(iface, -w, -segmentDirection[g], mode, keep, bsdf, side, out Attenuation crossed);
                                if ((bool)passed)
                                {
                                    passed *= beta * segmentWeight[g] * Through(media[next], media[next].thickness, segmentDirection[g]);
                                    if (weighed)
                                    {
                                        passed *= Strategy(runForward, runReverse, runCount, iface.PDF(-w, -segmentDirection[g], mode, BxDFReflTransFlags.All),
                                                           iface.PDF(-segmentDirection[g], -w, !mode, BxDFReflTransFlags.All), g, linkForward, linkReverse,
                                                           linkSpecular);
                                    }
                                    sum += passed;
                                    if (keep)
                                    {
                                        Attenuation path = PolarisedFacet.Compose(chain, true, crossed, fromCamera);
                                        StackBxDF.Accumulate(ref matrices, ref any, PolarisedFacet.Compose(path, true, segmentMatrix[g], fromCamera),
                                                             passed, directionExit, directionEnter);
                                    }
                                }
                            }
                        }
                    }
                    Float uc = r();
                    Point2f u = default;
                    u.x = r();
                    u.y = r();
                    Hand(iface, beta, chain, keep);
                    pstd.optional<BSDFSample> bs = Draw(iface, -w, uc, u, mode, BxDFReflTransFlags.All, keep, bsdf, side, fromCamera, false,
                                                        out Attenuation scattering);
                    if (!bs || !(bool)bs.value().f || bs.value().pdf == 0 || bs.value().wi.z == 0)
                    {
                        break;
                    }
                    beta *= bs.value().f * AbsCosTheta(bs.value().wi) / bs.value().pdf;
                    if (keep)
                    {
                        chain = PolarisedFacet.Compose(chain, true, scattering, fromCamera);
                    }
                    // The run of the walk: a reflection with a value begins one, a mirror leaves it as it is, and an interface with
                    // a density that lets the walk through with a value goes on with it; anything else ends it
                    if (!bs.value().IsSpecular())
                    {
                        if (bs.value().IsReflection())
                        {
                            run = Exact(iface);
                            runCount = 1;
                            runForward[0] = bs.value().pdf;
                            runReverse[0] = 0;
                        }
                        else if (run && Exact(iface) && runCount < most)
                        {
                            runForward[runCount] = bs.value().pdf;
                            // (the chain meets the interface from where the walk goes, and leaves it back along the walk)
                            runReverse[runCount] = iface.PDF(bs.value().wi, -w, !mode, BxDFReflTransFlags.All);
                            ++runCount;
                        }
                        else
                        {
                            run = false;
                        }
                    }
                    else if (!bs.value().IsReflection())
                    {
                        run = false;
                    }
                    w = bs.value().wi;
                    pure &= bs.value().IsSpecular();
                    if (bs.value().IsTransmission())
                    {
                        if (j == (up ? 0 : count - 1))
                        {
                            break;
                        }
                        medium = up ? medium - 1 : medium + 1;
                        depth = up ? media[medium].thickness : 0;
                    }
                    else
                    {
                        depth = up ? 0 : current.thickness;
                    }
                }
            }
            return sum / nSamples;
        }

        public override pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if (polarisedInside && !carriedPolarised)
            {
                return Inside(wo, uc, u, mode, sampleFlags);
            }
            return Sample(carriedFrame, wo, uc, u, mode, sampleFlags, carriedPolarised, out bool _, out Attenuation _);
        }

        public override pstd.optional<BSDFSample> Sample_f(BSDF bsdf, Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags,
                                                           out bool polarised, out Attenuation matrix)
        {
            return Sample(bsdf, wo, uc, u, mode, sampleFlags, true, out polarised, out matrix);
        }

        // The sample and, with `keep`, the matrix of its walk
        private pstd.optional<BSDFSample> Sample(BSDF bsdf, Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags, bool keep,
                                                 out bool polarised, out Attenuation matrix)
        {
            polarised = false;
            matrix = default;
            bool fromCamera = mode == TransportMode.Radiance;
            bool flip = false;
            if (twoSided && wo.z < 0)
            {
                wo = -wo;
                flip = true;
            }
            if (wo.z == 0)
            {
                return default;
            }
            int count = interfaces.Length;
            bool enterTop = wo.z > 0;
            int enter = enterTop ? 0 : count - 1;
            Context(keep, bsdf, flip ? -1 : 1, fromCamera);
            pstd.optional<BSDFSample> first = Draw(interfaces[enter], wo, uc, u, mode, BxDFReflTransFlags.All, keep, bsdf, flip ? -1 : 1, fromCamera, false,
                                                   out Attenuation chain);
            if (!first || !(bool)first.value().f || first.value().pdf == 0 || first.value().wi.z == 0)
            {
                return default;
            }
            BSDFSample bs = first.value();
            Float side = flip ? -1 : 1;
            if (bs.IsReflection() || count == 1)
            {
                BxDFReflTransFlags kind = bs.IsReflection() ? BxDFReflTransFlags.Reflection : BxDFReflTransFlags.Transmission;
                if ((sampleFlags & kind) == 0)
                {
                    return default;
                }
                if (flip)
                {
                    bs.wi = -bs.wi;
                }
                bs.pdfIsProportional = true;
                polarised = keep;
                matrix = chain;
                return bs;
            }

            // (a stream of its own: the first interface is given the same numbers, and one that walks would seed its stream as this one)
            RNG rng = new RNG(Hash(GetOptions().seed, wo, 0x6c737461636bUL), Hash(uc, u));
            Float r() { return std.min<Float>(rng.Uniform<Float>(), OneMinusEpsilon); }

            // (the weight of the walk is all that the sample is used for: pdfIsProportional)
            SampledSpectrum beta = bs.f * AbsCosTheta(bs.wi) / bs.pdf;
            Vector3f w = bs.wi;
            bool specular = bs.IsSpecular();
            int medium = enterTop ? 0 : count - 2;
            Float depth = enterTop ? 0 : media[medium].thickness;
            for (int step = 0; step < maxDepth; ++step)
            {
                if (keep)
                {
                    StackBxDF.Rebalance(ref beta, ref chain);
                }
                Float largest = beta.MaxComponentValue();
                if (step > 3 && largest < 0.25f)
                {
                    Float q = std.max<Float>(0, 1 - largest);
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
                LayerMedium current = media[medium];
                bool up = w.z > 0;
                Float toBoundary = up ? depth : current.thickness - depth;
                if (current.sigmaS > 0)
                {
                    Float flight = SampleExponential(r(), current.sigmaS / AbsCosTheta(w));
                    if (flight < toBoundary)
                    {
                        depth += up ? -flight : flight;
                        beta *= Exp(-current.sigmaA * (flight / AbsCosTheta(w))) * current.albedo;
                        HGPhaseFunction phase = new HGPhaseFunction(current.g);
                        Point2f up2 = default;
                        up2.x = r();
                        up2.y = r();
                        pstd.optional<PhaseFunctionSample> scattered = phase.Sample_p(-w, up2);
                        if (!scattered || scattered.value().pdf == 0 || scattered.value().wi.z == 0)
                        {
                            return default;
                        }
                        beta *= scattered.value().p / scattered.value().pdf;
                        w = scattered.value().wi;
                        specular = false;
                        if (keep)
                        {
                            chain = PolarisedFacet.Compose(chain, true, Attenuation.Lambert(new SampledSpectrum(1f)), fromCamera);
                        }
                        continue;
                    }
                }
                if ((bool)current.sigmaA)
                {
                    beta *= Exp(-current.sigmaA * (toBoundary / AbsCosTheta(w)));
                }
                int j = up ? medium : medium + 1;
                Float uc2 = r();
                Point2f u2 = default;
                u2.x = r();
                u2.y = r();
                Hand(interfaces[j], beta, chain, keep);
                pstd.optional<BSDFSample> next = Draw(interfaces[j], -w, uc2, u2, mode, BxDFReflTransFlags.All, keep, bsdf, side, fromCamera, false,
                                                      out Attenuation scattering);
                if (!next || !(bool)next.value().f || next.value().pdf == 0 || next.value().wi.z == 0)
                {
                    return default;
                }
                beta *= next.value().f * AbsCosTheta(next.value().wi) / next.value().pdf;
                if (keep)
                {
                    chain = PolarisedFacet.Compose(chain, true, scattering, fromCamera);
                }
                w = next.value().wi;
                specular &= next.value().IsSpecular();
                if (next.value().IsTransmission())
                {
                    if (j == (up ? 0 : count - 1))
                    {
                        // The walk has left the layers
                        bool reflection = SameHemisphere(wo, w);
                        if ((sampleFlags & (reflection ? BxDFReflTransFlags.Reflection : BxDFReflTransFlags.Transmission)) == 0)
                        {
                            return default;
                        }
                        BxDFFlags flags = reflection ? BxDFFlags.Reflection : BxDFFlags.Transmission;
                        flags = flags | (specular ? BxDFFlags.Specular : BxDFFlags.Glossy);
                        SampledSpectrum value = beta / AbsCosTheta(w);
                        if (flip)
                        {
                            w = -w;
                        }
                        polarised = keep;
                        matrix = chain;
                        return new BSDFSample(value, w, 1, flags, 1f, true);
                    }
                    medium = up ? medium - 1 : medium + 1;
                    depth = up ? media[medium].thickness : 0;
                }
                else
                {
                    depth = up ? 0 : current.thickness;
                }
            }
            return default;
        }

        // The asymmetry of the Henyey-Greenstein lobe that stands for a GGX reflection of roughness alpha, and the roughness
        // for an asymmetry (the fit of the paper, its equation 15)
        private static double Asymmetry(double alpha)
        {
            return -0.085 + 1.085 / (1 + Math.Pow(alpha / 0.5, 1.3));
        }

        private static double Roughness(double g)
        {
            return 0.5 * Math.Pow(Math.Max(0.0, 1.085 / (g + 0.085) - 1), 1 / 1.3);
        }

        // A lobe that goes into a medium of `ratio` times the index: its width goes with the angles, which Snell's law
        // divides by the ratio
        private static double Refracted(double g, double ratio)
        {
            return 1 - Math.Clamp((1 - g) / (ratio * ratio), 0.0, 1.0);
        }

        // m = m * [a b; c d]
        private static void Multiply(double* m, double a, double b, double c, double d)
        {
            double m0 = m[0] * a + m[1] * c, m1 = m[0] * b + m[1] * d, m2 = m[2] * a + m[3] * c, m3 = m[2] * b + m[3] * d;
            m[0] = m0;
            m[1] = m1;
            m[2] = m2;
            m[3] = m3;
        }

        // The lobes of the stack for light from wo (the transfer matrices of the header). For each interface, the energy and
        // the roughness of the lobe of its reflection (roughness 0: none, or a mirror, which has no density); the energy
        // that leaves as a diffuse lobe; and, for a stack that lets light through, the energy, the asymmetry and the cosine
        // of the transmitted lobe, with the index of the far side over that of the near side.
        private void Lobes(Float cosine, bool fromTop, double* energy, double* roughness, out double diffuse, out double transmitted,
                           out double transmittedAsymmetry, out double index)
        {
            int count = interfaces.Length;
            double* E = stackalloc double[4];
            double* G = stackalloc double[4];
            E[0] = 1; E[1] = 0; E[2] = 0; E[3] = 1;
            G[0] = 1; G[1] = 0; G[2] = 0; G[3] = 1;
            double mu = Math.Min(1.0, Math.Abs((double)cosine)), first = 1, before = 0, beforeG = 0;
            diffuse = 0;
            transmitted = 0;
            transmittedAsymmetry = 0;
            index = 1;
            for (int i = 0; i < count; ++i)
            {
                energy[i] = 0;
                roughness[i] = 0;
            }
            for (int i = 0; i < count; ++i)
            {
                int j = fromTop ? i : count - 1 - i;
                BxDF iface = interfaces[j];
                while (iface is AnyBxDF any)
                {
                    iface = any.inner;
                }
                double after, afterG;
                bool opaque = true, lambertian = false;
                DielectricBxDF dielectric = iface as DielectricBxDF;
                MultiScatterDielectricBxDF walked = iface as MultiScatterDielectricBxDF;
                if (dielectric != null || walked != null)
                {
                    Float relative = dielectric != null ? dielectric.Eta() : walked.Eta();
                    MicrofacetDistribution facets = dielectric != null ? dielectric.Distribution() : walked.Distribution();
                    double eta = fromTop ? relative : 1 / (double)relative;
                    double alpha = Math.Sqrt((double)facets.AlphaX() * facets.AlphaY());
                    double sine2 = (1 - mu * mu) / (eta * eta);
                    if (sine2 >= 1)
                    {
                        // (all of the mean direction is reflected)
                        after = (E[2] + E[3]) / (E[0] + E[1]);
                        double gReflected = Asymmetry(alpha);
                        afterG = (G[2] + G[3] * gReflected) / (G[0] + G[1] * gReflected);
                    }
                    else
                    {
                        opaque = false;
                        double muBelow = Math.Sqrt(1 - sine2);
                        double reflectance = Math.Clamp((double)FrDielectric((Float)mu, (Float)eta), 1e-6, 1 - 1e-6), transmittance = 1 - reflectance;
                        double gReflected = Asymmetry(alpha);
                        // (the roughness that a refraction shows: a facet tilted by an angle turns the refracted direction by this
                        // much of what it turns the reflected one)
                        double down = 0.5 * Math.Abs(1 - mu / (eta * muBelow)), back = 0.5 * Math.Abs(1 - muBelow * eta / mu);
                        double below = Math.Max(1e-6, Refracted(first, eta) * Asymmetry(down * alpha));
                        double gDown = below / first;
                        double gBack = Math.Max(1e-6, Refracted(below, 1 / eta) * Asymmetry(back * alpha)) / below;
                        Multiply(E, 1 / transmittance, -reflectance / transmittance, reflectance / transmittance,
                                 (transmittance * transmittance - reflectance * reflectance) / transmittance);
                        double tg = transmittance * gDown, rg = reflectance * gReflected;
                        Multiply(G, 1 / tg, -rg / tg, rg / tg, (tg * transmittance * gBack - rg * rg) / tg);
                        after = E[2] / E[0];
                        afterG = G[2] / G[0];
                        mu = muBelow;
                        first = below;
                        index *= eta;
                    }
                }
                else if (iface is BraggLayerBxDF mirror)
                {
                    // A Bragg mirror: it reflects as a rough mirror, and what it lets through goes straight on
                    opaque = false;
                    mirror.Means((Float)mu, fromTop, out Float up, out Float through);
                    double reflectance = Math.Clamp((double)up, 1e-6, 1 - 1e-6), transmittance = Math.Clamp((double)through, 1e-6, 1.0);
                    double gReflected = Asymmetry(mirror.Alpha());
                    Multiply(E, 1 / transmittance, -reflectance / transmittance, reflectance / transmittance,
                             (transmittance * transmittance - reflectance * reflectance) / transmittance);
                    double rg = reflectance * gReflected;
                    Multiply(G, 1 / transmittance, -rg / transmittance, rg / transmittance, (transmittance * transmittance - rg * rg) / transmittance);
                    after = E[2] / E[0];
                    afterG = G[2] / G[0];
                }
                else if (IsTransmissive(iface.Flags()))
                {
                    // Anything else that lets light through: taken as letting half of it straight through and reflecting
                    // the rest diffusely
                    opaque = false;
                    Multiply(E, 2, -1, 1, 0);
                    Multiply(G, 2, 0, 0, 0.5);
                    after = E[2] / E[0];
                    afterG = G[2] / G[0];
                    lambertian = true;
                }
                else
                {
                    // An interface that lets nothing through: a conductor, a diffuse base, or anything else, taken as diffuse
                    double reflectance = 0.5, gReflected = 0;
                    lambertian = true;
                    if (iface is ConductorBxDF conductor)
                    {
                        reflectance = FrComplex((Float)mu, conductor.Eta(), conductor.K()).Average();
                        gReflected = Asymmetry(Math.Sqrt((double)conductor.Distribution().AlphaX() * conductor.Distribution().AlphaY()));
                        lambertian = false;
                    }
                    else if (iface is MultiScatterConductorBxDF metal)
                    {
                        reflectance = FrComplex((Float)mu, metal.Eta(), metal.K()).Average();
                        gReflected = Asymmetry(Math.Sqrt((double)metal.Distribution().AlphaX() * metal.Distribution().AlphaY()));
                        lambertian = false;
                    }
                    else if (iface is DiffuseBxDF base_)
                    {
                        reflectance = base_.Reflectance().Average();
                    }
                    // (the reflectance of the layers above it with this under them: the paper's equation 24)
                    after = (E[2] + E[3] * reflectance) / (E[0] + E[1] * reflectance);
                    afterG = (G[2] + G[3] * reflectance * gReflected) / (G[0] + G[1] * reflectance * gReflected);
                }
                double share = Math.Max(0.0, after - before);
                if (lambertian)
                {
                    diffuse += share;
                }
                else if (share > 0)
                {
                    double g = Math.Clamp((afterG - beforeG) / share, 0.0, 1.0);
                    if (g <= Asymmetry(1))
                    {
                        diffuse += share;
                    }
                    else
                    {
                        energy[j] = share;
                        roughness[j] = Math.Min(1.0, Roughness(g));
                    }
                }
                before = after;
                beforeG = afterG;
                if (opaque)
                {
                    return;
                }
                if (i < count - 1)
                {
                    // The medium under the interface: what crosses it without an event keeps its lobe, and what it scatters is
                    // given to the diffuse lobe
                    LayerMedium medium = media[fromTop ? j : j - 1];
                    double length = medium.thickness / mu;
                    double kept = Math.Exp(-(medium.sigmaA.Average() + medium.sigmaS) * length);
                    if (medium.sigmaS > 0)
                    {
                        diffuse += (1 - Math.Exp(-medium.sigmaS * length)) * medium.albedo.Average() / E[0];
                    }
                    kept = Math.Max(kept, 1e-6);
                    Multiply(E, 1 / kept, 0, 0, kept);
                    Multiply(G, 1 / kept, 0, 0, kept);
                }
            }
            transmitted = 1 / E[0];
            transmittedAsymmetry = Math.Clamp(E[0] / G[0], 0.0, 0.999);
        }

        public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            Context(false, default, 1, true);
            if (twoSided && wo.z < 0)
            {
                wo = -wo;
                wi = -wi;
            }
            if (wo.z == 0 || wi.z == 0)
            {
                return 0;
            }
            bool reflection = SameHemisphere(wo, wi);
            if ((sampleFlags & (reflection ? BxDFReflTransFlags.Reflection : BxDFReflTransFlags.Transmission)) == 0)
            {
                return 0;
            }
            int count = interfaces.Length;
            double* energy = stackalloc double[count];
            double* roughness = stackalloc double[count];
            Lobes(wo.z, wo.z > 0, energy, roughness, out double diffuse, out double transmitted, out double transmittedAsymmetry, out double index);
            double total = diffuse + transmitted, density = 0;
            // (always in the upper hemisphere for the microfacet distributions)
            Vector3f o = wo.z > 0 ? wo : -wo, i = wo.z > 0 ? wi : -wi;
            for (int k = 0; k < count; ++k)
            {
                total += energy[k];
                if (reflection && energy[k] > 0 && roughness[k] >= 1e-3)
                {
                    Vector3f wm = o + i;
                    if (LengthSquared(wm) > 0)
                    {
                        wm = Normalize(wm);
                        TrowbridgeReitzDistribution lobe = new TrowbridgeReitzDistribution((Float)roughness[k], (Float)roughness[k]);
                        density += energy[k] * lobe.PDF(o, wm) / (4 * AbsDot(o, wm));
                    }
                }
            }
            bool through = transmitted > 0;
            if (reflection)
            {
                density += diffuse * AbsCosTheta(wi) * InvPi;
            }
            else if (through)
            {
                // The lobe around the direction that Snell's law gives through the whole stack
                Float scale = (Float)(1 / index);
                Float sine2 = (1 - Sqr(wo.z)) * Sqr(scale);
                Vector3f mean = -wo;
                if (sine2 < 1)
                {
                    mean = new Vector3f(-wo.x * scale, -wo.y * scale, (wo.z > 0 ? -1 : 1) * SafeSqrt(1 - sine2));
                }
                // (pbrt's function has the cosine between the direction back and the new one)
                density += transmitted * HenyeyGreenstein(-Dot(mean, wi), (Float)transmittedAsymmetry);
            }
            // (a part that is never zero where the stack has a value: the lobes are approximate)
            // (through the stack too whenever every interface lets light through: beyond the critical angle of a rough dielectric
            // the mean direction is reflected, the lobes have nothing through it, and its facets still let some light out)
            bool passes = true;
            for (int k = 0; k < count; ++k)
            {
                passes &= IsTransmissive(interfaces[k].Flags());
            }
            Float floor = reflection ? AbsCosTheta(wi) * InvPi : (passes ? Inv4Pi : 0);
            if (!(total > 0) || double.IsNaN(density))
            {
                return floor;
            }
            return Lerp(0.85f, floor, (Float)(density / total));
        }

        public override string ToString()
        {
            return StringPrintf("[ LayerStackBxDF interfaces: %d maxDepth: %d nSamples: %d twoSided: %s ]", interfaces.Length, maxDepth, nSamples, twoSided);
        }

        private BxDF[] interfaces;
        private LayerMedium[] media;
        private int maxDepth, nSamples;
        private bool twoSided;
    }
}

namespace pbrt
{
    // The layers of pbrt's coated materials as one flat stack: the same interfaces and the same medium (pbrt's medium has an
    // extinction of 1 for the unit of thickness, and only absorbs when its albedo is zero)
    public static unsafe class CoatedStack
    {
        public static LayerStackBxDF Create(BxDF top, BxDF bottom, Float thickness, SampledSpectrum albedo, Float g, int maxDepth, int nSamples,
                                            bool polarisedInside)
        {
            LayerMedium medium = new LayerMedium();
            medium.thickness = thickness;
            medium.g = g;
            if ((bool)albedo)
            {
                medium.sigmaS = 1;
                medium.albedo = albedo;
                medium.sigmaA = new SampledSpectrum(0f);
            }
            else
            {
                medium.sigmaS = 0;
                medium.albedo = new SampledSpectrum(0f);
                medium.sigmaA = new SampledSpectrum(1f);
            }
            LayerStackBxDF stack = new LayerStackBxDF(new BxDF[] { top, bottom }, new LayerMedium[] { medium }, maxDepth, nSamples, true);
            stack.polarisedInside = polarisedInside;
            return stack;
        }
    }

    // CoatedDiffuse2Material Definition
    // "coateddiffuse2": "coateddiffuse" with its parameters, evaluated as a flat stack (LayerStackBxDF)
    public sealed unsafe class CoatedDiffuse2Material : Material
    {
        public CoatedDiffuse2Material(CoatedDiffuseMaterial coated)
        {
            this.coated = coated;
        }

        public override bool CanEvaluateTextures<TextureEvaluator>(TextureEvaluator texEval)
        {
            return coated.CanEvaluateTextures(texEval);
        }

        public override BSDF GetBSDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                       ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            CoatedDiffuseBxDF layers = coated.GetBxDF(texEval, ctx, ref lambda);
            LayerStackBxDF stack = CoatedStack.Create(layers.TopLayer(), layers.BottomLayer(), layers.Thickness(), layers.Albedo(), layers.G(),
                                                      layers.MaxDepth(), layers.NSamples(), polarisedInside);
            return new BSDF(ctx.n, ctx.ns, ctx.dpdus, buf.Alloc(stack));
        }

        public static string Name() { return "CoatedDiffuse2Material"; }

        public override FloatTexture GetDisplacement() { return coated.GetDisplacement(); }
        public override Image GetNormalMap() { return coated.GetNormalMap(); }
        public override bool HasSubsurfaceScattering() { return false; }

        public override BSSRDF GetBSSRDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                           ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            return null;
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ CoatedDiffuse2Material %s ]", coated.ToString());
        }

        public static CoatedDiffuse2Material Create(TextureParameterDictionary parameters, Image normalMap, FileLoc loc, Allocator alloc)
        {
            CoatedDiffuse2Material material = new CoatedDiffuse2Material(CoatedDiffuseMaterial.Create(parameters, normalMap, loc, alloc));
            material.polarisedInside = parameters.GetOneBool("polarisedinside", false);
            return material;
        }

        private CoatedDiffuseMaterial coated;
        private bool polarisedInside;
    }

    // CoatedConductor2Material Definition
    // "coatedconductor2": "coatedconductor" with its parameters, evaluated as a flat stack (LayerStackBxDF)
    public sealed unsafe class CoatedConductor2Material : Material
    {
        public CoatedConductor2Material(CoatedConductorMaterial coated)
        {
            this.coated = coated;
        }

        public override bool CanEvaluateTextures<TextureEvaluator>(TextureEvaluator texEval)
        {
            return coated.CanEvaluateTextures(texEval);
        }

        public override BSDF GetBSDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                       ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            CoatedConductorBxDF layers = coated.GetBxDF(texEval, ctx, ref lambda);
            LayerStackBxDF stack = CoatedStack.Create(layers.TopLayer(), layers.BottomLayer(), layers.Thickness(), layers.Albedo(), layers.G(),
                                                      layers.MaxDepth(), layers.NSamples(), polarisedInside);
            return new BSDF(ctx.n, ctx.ns, ctx.dpdus, buf.Alloc(stack));
        }

        public static string Name() { return "CoatedConductor2Material"; }

        public override FloatTexture GetDisplacement() { return coated.GetDisplacement(); }
        public override Image GetNormalMap() { return coated.GetNormalMap(); }
        public override bool HasSubsurfaceScattering() { return false; }

        public override BSSRDF GetBSSRDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                           ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            return null;
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ CoatedConductor2Material %s ]", coated.ToString());
        }

        public static CoatedConductor2Material Create(TextureParameterDictionary parameters, Image normalMap, FileLoc loc, Allocator alloc)
        {
            CoatedConductor2Material material = new CoatedConductor2Material(CoatedConductorMaterial.Create(parameters, normalMap, loc, alloc));
            material.polarisedInside = parameters.GetOneBool("polarisedinside", false);
            return material;
        }

        private CoatedConductorMaterial coated;
        private bool polarisedInside;
    }

    // LayeredMaterial Definition
    // "layered": any number of interfaces with a medium between each two of them.
    //
    //   Material "layered" "string interfaces" [ "dielectric" "dielectric" "diffuse" ] "float eta" [ 1.5 1.33 ]
    //       "float roughness" [ 0 0.2 0 ] "float thickness" [ 0.2 0.5 ] "rgb absorption" [ 0 0 0  2 0.3 0.1 ]
    //
    //   "string interfaces"      from the top: "dielectric" any number of times, then at most one "conductor" or "diffuse", which
    //                            is the base. Without a base the stack lets light through.
    //   "float eta"              for each dielectric interface, the index of what is under it (above the first: 1). With the
    //                            number of the interface, "spectrum eta2" gives an index that depends on the wavelength: the
    //                            path then keeps one wavelength, as pbrt does for a dielectric that disperses.
    //   "spectrum reflectance"   of a diffuse base (0.5), or the colour of a conductor base in place of its indices
    //   "spectrum conductor.eta", "spectrum conductor.k"   of a conductor base (copper)
    //   "bool multiscatter"      (false) rough interfaces with every reflection between their facets, in place of pbrt's
    //                            microfacet models, which keep the first and lose the rest
    // For each interface, and for each medium (one fewer than the interfaces, in the unit of the thickness), as a list with a
    // value for each (one value: for all), as a texture for all, or, with the number of the interface or medium after the
    // name ("absorption2"), as a value or a texture for that one:
    //   "float roughness"        of the interface (0); "bool remaproughness" (true)
    //   "float thickness" (0.01), "spectrum absorption" (0), "float scattering" (0), "spectrum albedo" (1: the colour that a
    //   scattering gives the light), "float g" (0)   of the medium
    //   "integer maxdepth" (1000000: the walks end by themselves, with roulette), "integer nsamples" (1)
    //   "bool polarisedinside"   (false) asked without polarisation, the walk still keeps the polarisation of the light
    //                            between its events and returns what unpolarised light gives
    public sealed unsafe class LayeredMaterial : Material
    {
        public LayeredMaterial(string[] kinds, Spectrum[] eta, FloatTexture[] roughness, bool remapRoughness, bool multiScatter, SpectrumTexture reflectance,
                               bool hasReflectance, Spectrum conductorEta, Spectrum conductorK, FloatTexture[] thickness, SpectrumTexture[] absorption,
                               FloatTexture[] scattering, SpectrumTexture[] albedo, FloatTexture[] g, int maxDepth, int nSamples, FloatTexture displacement,
                               Image normalMap)
        {
            this.kinds = kinds;
            this.eta = eta;
            this.roughness = roughness;
            this.remapRoughness = remapRoughness;
            this.multiScatter = multiScatter;
            this.reflectance = reflectance;
            this.hasReflectance = hasReflectance;
            this.conductorEta = conductorEta;
            this.conductorK = conductorK;
            this.thickness = thickness;
            this.absorption = absorption;
            this.scattering = scattering;
            this.albedo = albedo;
            this.g = g;
            this.maxDepth = maxDepth;
            this.nSamples = nSamples;
            this.displacement = displacement;
            this.normalMap = normalMap;
        }

        public override bool CanEvaluateTextures<TextureEvaluator>(TextureEvaluator texEval)
        {
            List<FloatTexture> floats = new List<FloatTexture>();
            List<SpectrumTexture> spectra = new List<SpectrumTexture>();
            floats.AddRange(roughness);
            floats.AddRange(thickness);
            floats.AddRange(scattering);
            floats.AddRange(g);
            spectra.AddRange(absorption);
            spectra.AddRange(albedo);
            spectra.Add(reflectance);
            return texEval.CanEvaluate(floats.ToArray(), spectra.ToArray());
        }

        public override BSDF GetBSDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                       ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            int count = kinds.Length;
            BxDF[] interfaces = new BxDF[count];
            Float above = 1;
            bool opaque = false;
            for (int i = 0; i < count; ++i)
            {
                Float alpha = texEval.Apply(roughness[i], ctx);
                if (remapRoughness)
                {
                    alpha = TrowbridgeReitzDistribution.RoughnessToAlpha(alpha);
                }
                TrowbridgeReitzDistribution distribution = new TrowbridgeReitzDistribution(alpha, alpha);
                bool walked = multiScatter && !distribution.EffectivelySmooth();
                if (kinds[i] == "dielectric")
                {
                    Float index = eta[i].Evaluate(lambda[0]);
                    if (!(eta[i] is ConstantSpectrum))
                    {
                        lambda.TerminateSecondary();
                    }
                    interfaces[i] = walked ? new MultiScatterDielectricBxDF(distribution, new MicrosurfaceHeightUniform(), index / above, 64)
                                           : new DielectricBxDF(index / above, distribution);
                    above = index;
                }
                else if (kinds[i] == "conductor")
                {
                    SampledSpectrum conductorIndex, conductorExtinction;
                    if (hasReflectance)
                    {
                        // (as pbrt: the indices that give this reflectance at normal incidence)
                        SampledSpectrum r = Clamp(texEval.Apply(reflectance, ctx, lambda), 0, 0.9999f);
                        conductorIndex = new SampledSpectrum(1f);
                        conductorExtinction = 2 * Sqrt(r) / Sqrt(ClampZero(new SampledSpectrum(1f) - r));
                    }
                    else
                    {
                        conductorIndex = conductorEta.Sample(lambda);
                        conductorExtinction = conductorK.Sample(lambda);
                    }
                    // (the base is in the medium above it)
                    conductorIndex /= above;
                    conductorExtinction /= above;
                    interfaces[i] = walked ? new MultiScatterConductorBxDF(distribution, new MicrosurfaceHeightUniform(), conductorIndex, conductorExtinction, 64)
                                           : new ConductorBxDF(distribution, conductorIndex, conductorExtinction);
                    opaque = true;
                }
                else
                {
                    interfaces[i] = new DiffuseBxDF(Clamp(texEval.Apply(reflectance, ctx, lambda), 0, 1));
                    opaque = true;
                }
            }
            LayerMedium[] media = new LayerMedium[count - 1];
            for (int i = 0; i < count - 1; ++i)
            {
                media[i].thickness = std.max<Float>(0, texEval.Apply(thickness[i], ctx));
                media[i].sigmaS = std.max<Float>(0, texEval.Apply(scattering[i], ctx));
                media[i].g = Clamp(texEval.Apply(g[i], ctx), -0.99f, 0.99f);
                media[i].sigmaA = ClampZero(texEval.Apply(absorption[i], ctx, lambda));
                media[i].albedo = Clamp(texEval.Apply(albedo[i], ctx, lambda), 0, 1);
            }
            LayerStackBxDF stack = new LayerStackBxDF(interfaces, media, maxDepth, nSamples, opaque);
            stack.polarisedInside = polarisedInside;
            return new BSDF(ctx.n, ctx.ns, ctx.dpdus, buf.Alloc(stack));
        }

        public static string Name() { return "LayeredMaterial"; }

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
            return pbrt.StringPrintf("[ LayeredMaterial interfaces: %d ]", kinds.Length);
        }

        public static LayeredMaterial Create(TextureParameterDictionary parameters, Image normalMap, FileLoc loc, Allocator alloc)
        {
            string[] kinds = parameters.GetStringArray("interfaces").ToArray();
            int count = kinds.Length, dielectrics = 0;
            if (count == 0)
            {
                pbrt.ErrorExit(loc, "\"layered\" needs \"string interfaces\": \"dielectric\" any number of times, then \"conductor\" or \"diffuse\" or nothing.");
            }
            for (int i = 0; i < count; ++i)
            {
                if (kinds[i] == "dielectric")
                {
                    ++dielectrics;
                }
                else if ((kinds[i] != "conductor" && kinds[i] != "diffuse") || i != count - 1)
                {
                    pbrt.ErrorExit(loc, pbrt.StringPrintf("%s: \"interfaces\" are \"dielectric\", and a last one may be \"conductor\" or \"diffuse\".", kinds[i]));
                }
            }
            List<Float> indices = parameters.GetFloatArray("eta");
            if (indices.Count != 0 && indices.Count != 1 && indices.Count != dielectrics)
            {
                pbrt.ErrorExit(loc, pbrt.StringPrintf("\"eta\" needs %d values (or one for all).", dielectrics));
            }
            Spectrum[] eta = new Spectrum[count];
            for (int i = 0; i < count; ++i)
            {
                Float constant = i >= dielectrics ? 1 : (indices.Count == 0 ? 1.5f : (indices.Count == 1 ? indices[0] : indices[i]));
                eta[i] = parameters.GetOneSpectrum("eta" + (i + 1), new ConstantSpectrum(constant), SpectrumType.Unbounded, alloc);
            }
            SpectrumTexture reflectance = parameters.GetSpectrumTextureOrNull("reflectance", SpectrumType.Albedo, alloc);
            bool hasReflectance = reflectance != null;
            if (reflectance == null)
            {
                reflectance = new SpectrumConstantTexture(new ConstantSpectrum(0.5f));
            }
            Spectrum conductorEta = parameters.GetOneSpectrum("conductor.eta", pbrt.GetNamedSpectrum("metal-Cu-eta"), SpectrumType.Unbounded, alloc);
            Spectrum conductorK = parameters.GetOneSpectrum("conductor.k", pbrt.GetNamedSpectrum("metal-Cu-k"), SpectrumType.Unbounded, alloc);
            LayeredMaterial material = new LayeredMaterial(kinds, eta, SpongeCakeMaterial.Floats(parameters, "roughness", count, 0, false, loc, alloc),
                                       parameters.GetOneBool("remaproughness", true), parameters.GetOneBool("multiscatter", false), reflectance,
                                       hasReflectance, conductorEta, conductorK,
                                       SpongeCakeMaterial.Floats(parameters, "thickness", count - 1, 0.01f, false, loc, alloc),
                                       SpongeCakeMaterial.Spectra(parameters, "absorption", count - 1, 0, SpectrumType.Unbounded, loc, alloc),
                                       SpongeCakeMaterial.Floats(parameters, "scattering", count - 1, 0, false, loc, alloc),
                                       SpongeCakeMaterial.Spectra(parameters, "albedo", count - 1, 1, SpectrumType.Albedo, loc, alloc),
                                       SpongeCakeMaterial.Floats(parameters, "g", count - 1, 0, false, loc, alloc),
                                       parameters.GetOneInt("maxdepth", 1000000), parameters.GetOneInt("nsamples", 1),
                                       parameters.GetFloatTextureOrNull("displacement", alloc), normalMap);
            material.polarisedInside = parameters.GetOneBool("polarisedinside", false);
            return material;
        }

        private bool polarisedInside;
        private string[] kinds;
        private Spectrum[] eta;
        private FloatTexture[] roughness, thickness, scattering, g;
        private SpectrumTexture[] absorption, albedo;
        private bool remapRoughness, multiScatter;
        private SpectrumTexture reflectance;
        private bool hasReflectance;
        private Spectrum conductorEta, conductorK;
        private int maxDepth, nSamples;
        private FloatTexture displacement;
        private Image normalMap;
    }
}
