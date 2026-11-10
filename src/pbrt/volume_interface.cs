// (not in pbrt-v4) volume_interface.cs
//
// A mock-up of the volume interfaces of Fong et al., "Production Volume Rendering" (SIGGRAPH 2017 course), over the media that
// Magi has. Nothing here is called by an integrator yet; it fixes the shape of the pieces that the volume work hangs on.
//
// Layers, from the material up:
//
//   IVolumeMaterial     Material.HasVolume / GetVolume (Fong: Material::CreateVolume), returns a Volume
//   Volume              what a renderer asks of a volume: Integrate, SegmentTransmittance, Connect (+ the queries of the aggregate)
//   MediumVolume        a straight-ray volume made of a Medium (homogeneous or heterogeneous, emissive or not, texture driven or not)
//   RefractiveVolume    a curved-ray volume: a Medium plus a refractive index texture (experimental/refractive-media)
//   VolumeAggregate     Fong listing 24: integration intervals over many volumes
//
// Integrate does not call back into the renderer. It returns the null collisions it passed and the event that ended the
// segment; the renderer replays them in order with the state updates it already has (BeginCollision, NullCollision, RealScatter).
// This keeps path state, polarisation and the bdpt emission vertices in the renderer and the tracking in the volume.
//
// Differences from the listings of the paper:
//   * the quantities are spectral (SampledSpectrum with the SampledWavelengths of the path), not a scalar Color
//   * RendererServices is not passed: the volume intersects nothing, the caller gives the end of the segment (tMax)
//   * the ShadingContext of the constructor is the Interaction given to Material.GetVolume

using System;
using System.Collections.Generic;
using static pbrt.pbrt;

namespace pbrt
{

    // ScatteringMode Definition: how many times a path scatters in a volume (Fong listings 10 and 14). After the first scatter in
    // a single scattering volume the volume presents its scattering as absorption without emission, so the path only attenuates.
    public enum ScatteringMode
    {
        Single = 0,
        Multiple = 1
    }

    // VolumeEnd Definition: how a segment through a volume ended
    public enum VolumeEnd
    {
        // The segment reached its end (tMax, or the boundary of a curved volume) without a real collision
        Escaped = 0,

        // A real absorption collision: the path ends
        Absorbed = 1,

        // A real scattering collision: the renderer samples the phase function
        Scattered = 2
    }

    // VolumeCollision Definition: a tentative collision of the tracker, null or real
    public struct VolumeCollision
    {
        public Point3f p;

        // The direction the path travels at the collision: ray.d for a straight volume, the tangent of the curve for a curved one
        public Vector3f incoming;

        public MediumProperties mp;
        public SampledSpectrum sigma_maj;

        // The majorant transmittance from the previous collision (or the start of the segment) to this one
        public SampledSpectrum T_maj;
    }

    // VolumeIntegration Definition: what Volume.Integrate returns besides the passed collisions (Fong listing 1: L, weight,
    // transmittance, P and wo are the renderer's to form from the replayed collisions)
    public struct VolumeIntegration
    {
        public VolumeEnd end;

        // Absorbed or Scattered: the collision that ended the segment
        public VolumeCollision terminal;

        // Escaped: the majorant transmittance from the last collision to the end of the segment
        public SampledSpectrum T_maj;

        // Escaped from a curved volume: the ray that leaves it at its boundary; the walk resumes there
        public bool exitsStraight;
        public Ray exitRay;

        // Escaped from a curved volume: the medium is left where the curve ended (it ended within rounding errors of its start), so the
        // renderer pops it before it intersects the scene from exitRay
        public bool popsMedium;

        // Scattered: the scatterBit of the single scattering volume that scattered (0 for a multiple scattering volume), for the
        // renderer to add to the set of the path
        public ulong scatterBit;
    }

    // VolumeConnection Definition: a connection from a vertex inside a volume to a target point outside it, when the paths of the
    // volume are not straight lines (only a refractive volume makes one)
    public struct VolumeConnection
    {
        // The direction the curve leaves the vertex along, and the direction it arrives at the target along
        public Vector3f leavingDirection;
        public Vector3f arrivingDirection;

        // The length of the curve, and how far its end is from the target (the residual of the solver)
        public Float arcLength;
        public Float endDistance;

        // dA_perp(arriving) / dOmega(leaving): replaces d^2 in the geometry term (it is d^2 for a straight path), and the same for
        // the edge walked the other way (the reverse density of bdpt)
        public Float areaJacobian;
        public Float areaJacobianReverse;

        // The number of paths the solver found between the points: the weight of next event estimation is count * cos / J * area
        public int pathCount;

        // The transmittance along the curve and the factors of the spectral MIS weights, as SegmentTransmittance has them
        public SampledSpectrum transmittance;
        public SampledSpectrum r_l;
        public SampledSpectrum r_u;

        // Where the curve crosses the boundary of the volume: the straight shadow ray to the light continues from here
        public Point3f exitPoint;

        // The direction of the curve at exitPoint and its arc length there: the boundary of the volume is found from them
        public Vector3f exitDirection;
        public Float exitLength;

        // The direction at the end of the curve, at the target
        public Point3f exitLeavingPoint;
    }

    // IVolumeMaterial Definition (Fong: Material::HasVolume, CreateVolume): what Material provides. GetVolume returns a Volume
    // instead of the Medium it returns today.
    public interface IVolumeMaterial
    {
        bool HasVolume();

        // The volume for the point where a path enters the material (a "volumescale" material makes one per entry point)
        Volume GetVolume(Interaction ctx);

        // The rank of the volume where volumes meet (the boundary stack), whether it adds up with the volumes it overlaps, and the
        // rank of its integrator where they do
        int VolumePriority();
        bool CanOverlap();
        int OverlapPriority();
    }

    // Volume Definition (Fong listings 1 and 25)
    public abstract unsafe class Volume
    {
        // ---- Integration (listing 1)

        // Samples the collisions of the volume along ray over [0, tMax] (in units of the length of ray.d). Null collisions go
        // into passed in the order they happen (the caller clears the list); the event that ends the segment is in result. u is
        // the random number for the first distance. scatteredIn is the set of single scattering volumes the path has scattered in (their scatterBit); a volume that is in it: a single
        // scattering volume then presents sigma_s as absorption (no scattering, no emission). Multiple scattering volumes ignore it.
        public abstract void Integrate(Ray ray, Float tMax, Float u, ref RNG rng, SampledWavelengths lambda, ulong scatteredIn,
                                       List<VolumeCollision> passed, out VolumeIntegration result,
                                       Func<Point3f, Point3f, Float> sceneChord = null);

        // The transmittance of one straight segment inside the volume (listing 1's Transmittance, for the part of a shadow ray
        // that lies in this volume: the shadow ray itself crosses surfaces and volumes and stays a loop of the integrator).
        // Updates T_ray and the factors r_l, r_u of the spectral MIS weights, as the transmittance estimator does today.
        public abstract void SegmentTransmittance(Ray ray, Float tMax, SampledWavelengths lambda, ref RNG rng, bool roulette,
                                                  ref SampledSpectrum T_ray, ref SampledSpectrum r_l, ref SampledSpectrum r_u);

        // A connection from a vertex inside the volume to a target outside it that is not a straight line. Returns false for a
        // volume whose connections are straight (every volume but a refractive one): the integrator then traces the usual shadow
        // ray, whose segments call SegmentTransmittance.
        public virtual bool Connect(Point3f vertex, Point3f target, SampledWavelengths lambda, Float time, ref RNG rng,
                                    out VolumeConnection connection, Func<Point3f, Point3f, Float> sceneChord = null)
        {
            connection = default;
            return false;
        }

        // ---- Queries (listing 25, what the aggregate and the estimators ask)

        public abstract SampledSpectrum Extinction(Point3f p, Float time, Float filterWidth, SampledWavelengths lambda);

        // The smallest and largest extinction in a region: the (min, max) of a node of the aggregate, the majorant of a cell, and
        // the control of residual ratio tracking
        public abstract void ExtinctionMinMax(Bounds3f region, SampledWavelengths lambda, out SampledSpectrum minimum,
                                              out SampledSpectrum maximum);

        public abstract PhaseFunction GetPhaseFunction(Point3f p, Float time, SampledWavelengths lambda);

        public abstract Bounds3f Bounds();

        // ---- Properties

        // The bit of this volume in PathState.scatteredIn if it scatters once per path (a SingleScattering volume), else 0
        public ulong scatterBit;

        // The rank of the integrator of the volume where several volumes overlap (Fong section 3.9)
        public int overlapPriority;

        // Whether the extinction is the same everywhere: the transmittance has a closed form
        public virtual bool IsHomogeneous() { return false; }

        // Whether the volume emits: the renderer gathers sigma_a * Le at its collisions
        public virtual bool IsEmissive() { return false; }

        // The emission as a light, for the strategies that sample the medium (bdpt); null if there is none
        public virtual VolumeEmissionLight EmissionLight() { return null; }

        // Whether the paths in the volume are curved
        public virtual bool IsRefractive() { return false; }
    }

    // VolumeBakeOptions Definition: how a texture driven volume trades exactness for the speed of a grid. With spacing 0 the texture
    // is evaluated at every tentative collision, with tight bounds from RegionBounds (unbiased). With a spacing the texture is diced
    // into bricks on first visit, on a lattice of that spacing, and the volume is the baked field (unbiased for the baked field; the
    // difference to the texture is measured and reported when the bricks are made).
    public struct VolumeBakeOptions
    {
        // Lattice spacing in medium space; 0 = do not bake; negative = from the finest feature of the texture
        // (a new FloatTexture.FinestScale()) divided by samplesPerFeature
        public Float spacing;
        public Float samplesPerFeature;
        public int brickSize;
        public bool cubic;
    }

    // IScatteringPolicy Definition: what a collision of a volume presents to the tracking. It is a type, so a volume is a single or a
    // multiple scattering volume by its class and there is no flag to test in the loop of the tracking.
    public interface IScatteringPolicy
    {
        static abstract MediumProperties Present(MediumProperties properties, bool alreadyScattered);

        // Whether the path has to remember that it scattered in the volume
        static abstract bool ScattersOnce { get; }
    }

    // MultipleScattering Definition: every collision is what the medium says (Fong listing 14)
    public struct MultipleScattering : IScatteringPolicy
    {
        public static MediumProperties Present(MediumProperties properties, bool alreadyScattered)
        {
            return properties;
        }

        public static bool ScattersOnce { get { return false; } }
    }

    // SingleScattering Definition: after the first scattering in the volume it only attenuates: sigma_s is presented as absorption, and
    // there is no emission (Fong listing 10)
    public struct SingleScattering : IScatteringPolicy
    {
        public static MediumProperties Present(MediumProperties properties, bool alreadyScattered)
        {
            if (alreadyScattered)
            {
                properties.sigma_a = properties.sigma_a + properties.sigma_s;
                properties.sigma_s = new SampledSpectrum(0.0f);
                properties.Le = new SampledSpectrum(0.0f);
            }
            return properties;
        }

        public static bool ScattersOnce { get { return true; } }
    }

    // HomogeneousVolume Definition: the same coefficients everywhere. The distance to a collision has a closed form, the majorant is the
    // extinction (there are no null collisions) and the transmittance of a segment is exp(-sigma_t d).
    public sealed unsafe class HomogeneousVolume<TScattering> : Volume where TScattering : struct, IScatteringPolicy
    {
        public HomogeneousVolume(Medium medium, Bounds3f bounds)
        {
            this.medium = medium;
            this.bounds = bounds;
        }

        public override void Integrate(Ray ray, Float tMax, Float u, ref RNG rng, SampledWavelengths lambda, ulong scatteredIn,
                                       List<VolumeCollision> passed, out VolumeIntegration result,
                                       Func<Point3f, Point3f, Float> sceneChord = null)
        {
            VolumeIntegration integration = default;
            integration.end = VolumeEnd.Escaped;
            integration.exitsStraight = true;
            Float length = Length(ray.d);
            Vector3f direction = ray.d / length;
            Float distance = tMax * length;
            MediumProperties properties = TScattering.Present(medium.SamplePoint(ray.o, lambda, ray.time), (scatteredIn & scatterBit) != 0);
            SampledSpectrum sigma_maj = properties.sigma_a + properties.sigma_s;
            if (sigma_maj[0] == 0)
            {
                // The hero wavelength never collides
                integration.T_maj = Transmission(sigma_maj, distance);
                result = integration;
                return;
            }
            Float tCurrent = 0;
            Float uNext = u;
            while (true)
            {
                Float tNext = tCurrent + SampleExponential(uNext, sigma_maj[0]);
                if (tNext >= distance)
                {
                    integration.T_maj = Transmission(sigma_maj, distance - tCurrent);
                    break;
                }
                VolumeCollision collision = default;
                collision.p = ray.o + tNext * direction;
                collision.incoming = ray.d;
                collision.mp = properties;
                collision.sigma_maj = sigma_maj;
                collision.T_maj = Transmission(sigma_maj, tNext - tCurrent);
                MediumEventType evt = MediumEvents.SampleEvent(properties, sigma_maj, rng.Uniform<Float>());
                if (evt == MediumEventType.Null)
                {
                    passed.Add(collision);
                    tCurrent = tNext;
                    uNext = rng.Uniform<Float>();
                    continue;
                }
                integration.end = evt == MediumEventType.Absorb ? VolumeEnd.Absorbed : VolumeEnd.Scattered;
                integration.terminal = collision;
                integration.scatterBit = evt == MediumEventType.Absorb ? 0UL : scatterBit;
                break;
            }
            result = integration;
        }

        public override void SegmentTransmittance(Ray ray, Float tMax, SampledWavelengths lambda, ref RNG rng, bool roulette,
                                                  ref SampledSpectrum T_ray, ref SampledSpectrum r_l, ref SampledSpectrum r_u)
        {
            MediumProperties properties = medium.SamplePoint(ray.o, lambda, ray.time);
            T_ray *= Transmission(properties.sigma_a + properties.sigma_s, tMax * Length(ray.d));
        }

        // exp(-sigma d) per wavelength, with an infinite distance and a zero coefficient giving 1 as in the tracker
        static SampledSpectrum Transmission(SampledSpectrum sigma, Float distance)
        {
            Float d = Float.IsInfinity(distance) ? Float.MaxValue : distance;
            SampledSpectrum T = new SampledSpectrum(1.0f);
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                if (sigma[i] != 0)
                {
                    T[i] = FastExp(-d * sigma[i]);
                }
            }
            return T;
        }

        public override SampledSpectrum Extinction(Point3f p, Float time, Float filterWidth, SampledWavelengths lambda)
        {
            MediumProperties properties = medium.SamplePoint(p, lambda, time);
            return properties.sigma_a + properties.sigma_s;
        }

        public override void ExtinctionMinMax(Bounds3f region, SampledWavelengths lambda, out SampledSpectrum minimum,
                                              out SampledSpectrum maximum)
        {
            SampledSpectrum sigma_t = Extinction(region.pMin + 0.5f * region.Diagonal(), 0, 0, lambda);
            minimum = sigma_t;
            maximum = sigma_t;
        }

        public override PhaseFunction GetPhaseFunction(Point3f p, Float time, SampledWavelengths lambda)
        {
            return medium.SamplePoint(p, lambda, time).phase;
        }

        public override Bounds3f Bounds() { return bounds; }

        public override bool IsHomogeneous() { return true; }

        public override bool IsEmissive() { return medium.IsEmissive(); }

        readonly Medium medium;
        readonly Bounds3f bounds;
    }

    // HeterogeneousVolume Definition: a coefficient that varies in space (a grid, a texture, a procedural field, a cloud, a composite).
    // Collisions are found by tracking a majorant with null collisions, and the transmittance by the estimator of the medium.
    public sealed unsafe class HeterogeneousVolume<TScattering> : Volume where TScattering : struct, IScatteringPolicy
    {
        public HeterogeneousVolume(Medium medium, Bounds3f bounds, VolumeBakeOptions bake)
        {
            this.medium = medium;
            this.bounds = bounds;
            this.bake = bake;
        }

        public override void Integrate(Ray ray, Float tMax, Float u, ref RNG rng, SampledWavelengths lambda, ulong scatteredIn,
                                       List<VolumeCollision> passed, out VolumeIntegration result,
                                       Func<Point3f, Point3f, Float> sceneChord = null)
        {
            VolumeIntegration integration = default;
            integration.end = VolumeEnd.Escaped;
            integration.exitsStraight = true;
            RNG localRng = rng;
            SampledSpectrum T_maj = medium.volumeIntegrator.tracker.SampleT_maj(
                ray, medium, tMax, u, ref localRng, lambda,
                (Point3f p, MediumProperties mp, SampledSpectrum sigma_maj, SampledSpectrum T_maj_) =>
                {
                    mp = TScattering.Present(mp, (scatteredIn & scatterBit) != 0);
                    VolumeCollision collision = default;
                    collision.p = p;
                    collision.incoming = ray.d;
                    collision.mp = mp;
                    collision.sigma_maj = sigma_maj;
                    collision.T_maj = T_maj_;
                    MediumEventType evt = MediumEvents.SampleEvent(mp, sigma_maj, localRng.Uniform<Float>());
                    if (evt == MediumEventType.Null)
                    {
                        passed.Add(collision);
                        return true;
                    }
                    integration.end = evt == MediumEventType.Absorb ? VolumeEnd.Absorbed : VolumeEnd.Scattered;
                    integration.terminal = collision;
                    integration.scatterBit = evt == MediumEventType.Absorb ? 0UL : scatterBit;
                    return false;
                });
            if (integration.end == VolumeEnd.Escaped)
            {
                integration.T_maj = T_maj;
            }
            rng = localRng;
            result = integration;
        }

        public override void SegmentTransmittance(Ray ray, Float tMax, SampledWavelengths lambda, ref RNG rng, bool roulette,
                                                  ref SampledSpectrum T_ray, ref SampledSpectrum r_l, ref SampledSpectrum r_u)
        {
            // The block of Integrator.ShadowRay that handles the medium of a segment
            SampledSpectrum T = T_ray;
            SampledSpectrum rl = r_l;
            SampledSpectrum ru = r_u;
            RNG localRng = rng;
            Float uT = localRng.Uniform<Float>();
            SampledSpectrum T_closed;
            if (medium.volumeIntegrator.transmittance.TryClosedForm(ray, medium, tMax, lambda, out T_closed))
            {
                T *= T_closed;
            }
            else if (medium.volumeIntegrator.transmittance.TryEstimate(ray, medium, tMax, ref localRng, lambda, ref T, ref rl, ref ru))
            {
            }
            else
            {
                SampledSpectrum T_maj = medium.volumeIntegrator.tracker.SampleT_maj(
                    ray, medium, tMax, uT, ref localRng, lambda,
                    (Point3f p, MediumProperties mp, SampledSpectrum sigma_maj, SampledSpectrum T_maj_) =>
                    {
                        SampledSpectrum sigma_n = ClampZero(sigma_maj - mp.sigma_a - mp.sigma_s);
                        medium.volumeIntegrator.transmittance.Step(ref T, ref rl, ref ru, T_maj_, sigma_maj, sigma_n, ref localRng);
                        if (roulette)
                        {
                            SampledSpectrum Tr = T / (rl + ru).Average();
                            if (Tr.MaxComponentValue() < 0.05f)
                            {
                                Float q = 0.75f;
                                if (localRng.Uniform<Float>() < q)
                                {
                                    T = new SampledSpectrum(0.0f);
                                }
                                else
                                {
                                    T /= 1 - q;
                                }
                            }
                        }
                        return (bool)T;
                    });
                medium.volumeIntegrator.transmittance.Finish(ref T, ref rl, ref ru, T_maj);
            }
            T_ray = T;
            r_l = rl;
            r_u = ru;
            rng = localRng;
        }

        public override SampledSpectrum Extinction(Point3f p, Float time, Float filterWidth, SampledWavelengths lambda)
        {
            MediumProperties properties = medium.SamplePoint(p, lambda, time);
            return properties.sigma_a + properties.sigma_s;
        }

        public override void ExtinctionMinMax(Bounds3f region, SampledWavelengths lambda, out SampledSpectrum minimum,
                                              out SampledSpectrum maximum)
        {
            // The extinction on a regular grid of points of the region (nine along each axis): the bounds are an estimate that the
            // grid of the medium refines, and only guide subdivision (the integrators sample with the majorants of the medium)
            minimum = new SampledSpectrum(Infinity);
            maximum = new SampledSpectrum(0.0f);
            for (int i = 0; i < 9; ++i)
            {
                for (int j = 0; j < 9; ++j)
                {
                    for (int k = 0; k < 9; ++k)
                    {
                        Point3f p = region.Lerp(new Point3f(i / 8.0f, j / 8.0f, k / 8.0f));
                        SampledSpectrum extinction = Extinction(p, 0, 0, lambda);
                        for (int c = 0; c < NSpectrumSamples; ++c)
                        {
                            minimum[c] = Math.Min(minimum[c], extinction[c]);
                            maximum[c] = Math.Max(maximum[c], extinction[c]);
                        }
                    }
                }
            }
        }

        public override PhaseFunction GetPhaseFunction(Point3f p, Float time, SampledWavelengths lambda)
        {
            return medium.SamplePoint(p, lambda, time).phase;
        }

        public override Bounds3f Bounds() { return bounds; }

        public override bool IsEmissive() { return medium.IsEmissive(); }

        public override VolumeEmissionLight EmissionLight() { return (medium as IEmissionMedium)?.EmissionLight(); }

        readonly Medium medium;
        readonly Bounds3f bounds;
        readonly VolumeBakeOptions bake;
    }

    // CompositeVolume Definition: volumes that overlap (Fong section 3.9). The particles of the volumes do not react to each other, so
    // the combination has the sum of their coefficients, one tracking runs over the sum of their majorants, and a collision
    // belongs to one volume, chosen by its scattering coefficient. Each volume keeps its own scattering mode: a single scattering
    // volume that the path has scattered in presents its sigma_s as absorption, and the others go on scattering.
    public sealed unsafe class CompositeVolume : Volume
    {
        public CompositeVolume(CompositeMedium composite)
        {
            this.composite = composite;
            transmittance = new HeterogeneousVolume<MultipleScattering>(composite, new Bounds3f(), default);
            Medium[] media = composite.Children();
            children = new Volume[media.Length];
            for (int i = 0; i < media.Length; ++i)
            {
                children[i] = VolumeFactory.For(media[i]);
            }
        }

        public override void Integrate(Ray ray, Float tMax, Float u, ref RNG rng, SampledWavelengths lambda, ulong scatteredIn,
                                       List<VolumeCollision> passed, out VolumeIntegration result,
                                       Func<Point3f, Point3f, Float> sceneChord = null)
        {
            VolumeIntegration integration = default;
            integration.end = VolumeEnd.Escaped;
            integration.exitsStraight = true;
            RNG localRng = rng;
            Medium[] media = composite.Children();
            SampledSpectrum T_maj = composite.volumeIntegrator.tracker.SampleT_maj(
                ray, composite, tMax, u, ref localRng, lambda,
                (Point3f p, MediumProperties summed, SampledSpectrum sigma_maj, SampledSpectrum T_maj_) =>
                {
                    MediumProperties total = default;
                    total.sigma_a = new SampledSpectrum(0.0f);
                    total.sigma_s = new SampledSpectrum(0.0f);
                    SampledSpectrum emission = new SampledSpectrum(0.0f);
                    MediumProperties[] properties = new MediumProperties[media.Length];
                    for (int i = 0; i < media.Length; ++i)
                    {
                        properties[i] = media[i].SamplePoint(p, lambda, ray.time);
                        if (children[i].scatterBit != 0 && (scatteredIn & children[i].scatterBit) != 0)
                        {
                            properties[i] = SingleScattering.Present(properties[i], true);
                        }
                        total.sigma_a += properties[i].sigma_a;
                        total.sigma_s += properties[i].sigma_s;
                        emission += properties[i].sigma_a * properties[i].Le;
                    }
                    total.Le = new SampledSpectrum(0.0f);
                    for (int c = 0; c < NSpectrumSamples; ++c)
                    {
                        total.Le[c] = total.sigma_a[c] > 0 ? emission[c] / total.sigma_a[c] : 0;
                    }
                    // The collision is with one volume, chosen by the scattering coefficient of the hero wavelength
                    int chosen = 0;
                    Float pick = localRng.Uniform<Float>() * total.sigma_s[0];
                    Float accumulated = 0;
                    for (int i = 0; i < media.Length; ++i)
                    {
                        if (properties[i].sigma_s[0] > 0)
                        {
                            chosen = i;
                            accumulated += properties[i].sigma_s[0];
                            if (pick < accumulated)
                            {
                                break;
                            }
                        }
                    }
                    total.phase = properties[chosen].phase;
                    VolumeCollision collision = default;
                    collision.p = p;
                    collision.incoming = ray.d;
                    collision.mp = total;
                    collision.sigma_maj = sigma_maj;
                    collision.T_maj = T_maj_;
                    MediumEventType evt = MediumEvents.SampleEvent(total, sigma_maj, localRng.Uniform<Float>());
                    if (evt == MediumEventType.Null)
                    {
                        passed.Add(collision);
                        return true;
                    }
                    if (evt == MediumEventType.Scatter)
                    {
                        // The collision is the chosen volume's: the weight of every wavelength and its density as a hero wavelength
                        // (the spectral MIS) use that volume's sigma_s, not the sum, whose ratios differ between the wavelengths when the
                        // volumes have different colours
                        collision.mp.sigma_s = properties[chosen].sigma_s;
                    }
                    integration.end = evt == MediumEventType.Absorb ? VolumeEnd.Absorbed : VolumeEnd.Scattered;
                    integration.terminal = collision;
                    integration.scatterBit = evt == MediumEventType.Absorb ? 0UL : children[chosen].scatterBit;
                    return false;
                });
            if (integration.end == VolumeEnd.Escaped)
            {
                integration.T_maj = T_maj;
            }
            rng = localRng;
            result = integration;
        }

        public override void SegmentTransmittance(Ray ray, Float tMax, SampledWavelengths lambda, ref RNG rng, bool roulette,
                                                  ref SampledSpectrum T_ray, ref SampledSpectrum r_l, ref SampledSpectrum r_u)
        {
            // The extinction of a single scattering volume does not depend on whether the path has scattered in it
            transmittance.SegmentTransmittance(ray, tMax, lambda, ref rng, roulette, ref T_ray, ref r_l, ref r_u);
        }

        public override SampledSpectrum Extinction(Point3f p, Float time, Float filterWidth, SampledWavelengths lambda)
        {
            SampledSpectrum sum = new SampledSpectrum(0.0f);
            for (int i = 0; i < children.Length; ++i)
            {
                sum += children[i].Extinction(p, time, filterWidth, lambda);
            }
            return sum;
        }

        public override void ExtinctionMinMax(Bounds3f region, SampledWavelengths lambda, out SampledSpectrum minimum,
                                              out SampledSpectrum maximum)
        {
            minimum = new SampledSpectrum(0.0f);
            maximum = new SampledSpectrum(0.0f);
            for (int i = 0; i < children.Length; ++i)
            {
                children[i].ExtinctionMinMax(region, lambda, out SampledSpectrum childMinimum, out SampledSpectrum childMaximum);
                minimum += childMinimum;
                maximum += childMaximum;
            }
        }

        public override PhaseFunction GetPhaseFunction(Point3f p, Float time, SampledWavelengths lambda)
        {
            return composite.SamplePoint(p, lambda, time).phase;
        }

        public override Bounds3f Bounds() { return new Bounds3f(); }

        public override bool IsEmissive() { return composite.IsEmissive(); }

        readonly CompositeMedium composite;
        readonly HeterogeneousVolume<MultipleScattering> transmittance;
        readonly Volume[] children;
    }

    // VolumeFactory Definition: the volume of a medium of the scene (until materials make them: Material.GetVolume). The scene says
    // "string scattering" "single" or "multiple" (the default) on the medium; the class of the volume follows from that and from
    // whether the medium is homogeneous.
    public static class VolumeFactory
    {
        public static Volume Create(Medium medium, ScatteringMode mode, Bounds3f bounds)
        {
            if (medium is RefractiveMedium)
            {
                return new RefractiveVolume((RefractiveMedium)medium);
            }
            if (medium is CompositeMedium)
            {
                return new CompositeVolume((CompositeMedium)medium);
            }
            bool homogeneous = medium is HomogeneousMedium;
            Volume volume;
            if (mode == ScatteringMode.Single)
            {
                volume = homogeneous ? new HomogeneousVolume<SingleScattering>(medium, bounds)
                                     : new HeterogeneousVolume<SingleScattering>(medium, bounds, default);
                volume.scatterBit = 1UL << (System.Threading.Interlocked.Increment(ref singleCount) % 64);
            }
            else
            {
                volume = homogeneous ? new HomogeneousVolume<MultipleScattering>(medium, bounds)
                                     : new HeterogeneousVolume<MultipleScattering>(medium, bounds, default);
            }
            return volume;
        }

        public static Volume For(Medium medium)
        {
            return cache.GetValue(medium, (Medium m) => Create(m, m.scattering, new Bounds3f()));
        }

        static int singleCount;

        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Medium, Volume> cache =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Medium, Volume>();
    }

    // RefractiveVolume Definition: a curved-ray volume. The coefficients, emission and phase function are those of the inner medium
    // (so it is homogeneous or heterogeneous, emissive or not, like any other); the paths follow the refractive index texture, which
    // has to be smooth (a value, a gradient and a Hessian). The boundary of the volume must have index 1, and a refractive volume
    // does not overlap other volumes.
    public sealed unsafe class RefractiveVolume : Volume
    {
        public RefractiveVolume(RefractiveMedium medium)
        {
            this.medium = medium;
        }

        public override bool IsRefractive() { return true; }

        // Collisions along the curved path (Pediredla et al.): the path is followed in arc length with the symplectic integrator of
        // RefractiveMedium, with one constant majorant. The collisions carry the tangent of the path as incoming. The segment ends
        // where the path leaves the region: the ray that finds the surface bounding the volume starts a little before that point.
        public override void Integrate(Ray ray, Float tMax, Float u, ref RNG rng, SampledWavelengths lambda, ulong scatteredIn,
                                       List<VolumeCollision> passed, out VolumeIntegration result,
                                       Func<Point3f, Point3f, Float> sceneChord = null)
        {
            VolumeIntegration integration = default;
            integration.end = VolumeEnd.Escaped;
            RNG localRng = rng;
            Vector3f direction = Normalize(ray.d);
            SampledSpectrum T_maj = medium.SampleT_majCurved(
                ray.o, direction, Infinity, u, ref localRng, lambda,
                (Point3f p, Vector3f tangent, MediumProperties mp, SampledSpectrum sigma_maj, SampledSpectrum T_maj_) =>
                {
                    VolumeCollision collision = default;
                    collision.p = p;
                    collision.incoming = tangent;
                    collision.mp = mp;
                    collision.sigma_maj = sigma_maj;
                    collision.T_maj = T_maj_;
                    MediumEventType evt = MediumEvents.SampleEvent(mp, sigma_maj, localRng.Uniform<Float>());
                    if (evt == MediumEventType.Null)
                    {
                        passed.Add(collision);
                        return true;
                    }
                    integration.end = evt == MediumEventType.Absorb ? VolumeEnd.Absorbed : VolumeEnd.Scattered;
                    integration.terminal = collision;
                    return false;
                }, out Point3f endPoint, out Vector3f endDirection, out Float endLength, null, sceneChord);
            rng = localRng;
            if (integration.end != VolumeEnd.Escaped)
            {
                result = integration;
                return;
            }
            integration.T_maj = T_maj;
            integration.exitsStraight = false;
            Bounds3f bounds = medium.Bounds();
            Float diagonal = Length(bounds.pMax - bounds.pMin);
            if (endLength < 1e-4f * diagonal)
            {
                // Within a few rounding errors of its start: there is no distance to find the boundary from. If the end is inside the
                // region it is a surface of the scene there, which the walk finds from the end, and the medium stays
                integration.popsMedium = !medium.Contains(endPoint + 2e-4f * diagonal * endDirection);
                integration.exitRay = endLength == 0 ? ray : new Ray(endPoint, endDirection, ray.time);
            }
            else
            {
                Float back = Math.Min(1e-3f * diagonal, 0.5f * endLength);
                integration.exitRay = new Ray(endPoint - back * endDirection, endDirection, ray.time);
            }
            result = integration;
        }

        public override void SegmentTransmittance(Ray ray, Float tMax, SampledWavelengths lambda, ref RNG rng, bool roulette,
                                                  ref SampledSpectrum T_ray, ref SampledSpectrum r_l, ref SampledSpectrum r_u)
        {
            // A straight segment through a refractive volume is blocked (its paths are curves); the segments after the boundary
            // are other volumes'
            T_ray = new SampledSpectrum(0.0f);
        }

        // The curve from a vertex inside the volume to a target (algorithm 3 of Pediredla et al.: one search from a random direction,
        // the count of the searches that find a path is the estimate of one over the probability of the path), and the transmittance
        // along it by tracking in the arc length
        public override bool Connect(Point3f vertex, Point3f target, SampledWavelengths lambda, Float time, ref RNG rng,
                                     out VolumeConnection connection, Func<Point3f, Point3f, Float> sceneChord = null)
        {
            connection = default;
            RefractiveConnectionOptions options = medium.ConnectionOptions(Distance(vertex, target));
            if (!medium.ConnectToPoint(vertex, target, options, ref rng, out RefractiveConnectionPath path, out int count))
            {
                return false;
            }
            connection.leavingDirection = path.startDirection;
            connection.arrivingDirection = path.endDirection;
            connection.arcLength = path.length;
            connection.endDistance = path.distance;
            connection.areaJacobian = path.areaJacobian;
            connection.pathCount = count;
            SampledSpectrum T_ray = new SampledSpectrum(1.0f);
            SampledSpectrum rl = new SampledSpectrum(1.0f);
            SampledSpectrum ru = new SampledSpectrum(1.0f);
            RNG localRng = rng;
            Float uT = localRng.Uniform<Float>();
            // A surface of the scene in the volume blocks the curve; the boundary of the volume, which the chord of the last step meets, does not
            ChordOcclusion occlusion = new ChordOcclusion();
            Func<Point3f, Point3f, Float> chordHit = ChordOcclusion.Chord(medium, sceneChord, occlusion);
            SampledSpectrum T_maj = medium.SampleT_majCurved(
                vertex, path.startDirection, path.length, uT, ref localRng, lambda,
                (Point3f p, Vector3f tangent, MediumProperties mp, SampledSpectrum sigma_maj, SampledSpectrum T_maj_) =>
                {
                    SampledSpectrum sigma_n = ClampZero(sigma_maj - mp.sigma_a - mp.sigma_s);
                    medium.volumeIntegrator.transmittance.Step(ref T_ray, ref rl, ref ru, T_maj_, sigma_maj, sigma_n, ref localRng);
                    return (bool)T_ray;
                }, out Point3f exitPoint, out Vector3f exitDirection, out Float exitLength, null, chordHit);
            medium.volumeIntegrator.transmittance.Finish(ref T_ray, ref rl, ref ru, T_maj);
            rng = localRng;
            if (occlusion.occluded)
            {
                return false;
            }
            connection.transmittance = T_ray;
            connection.r_l = rl;
            connection.r_u = ru;
            connection.exitPoint = exitPoint;
            connection.exitDirection = exitDirection;
            connection.exitLength = exitLength;
            return (bool)T_ray;
        }

        public override SampledSpectrum Extinction(Point3f p, Float time, Float filterWidth, SampledWavelengths lambda)
        {
            MediumProperties properties = medium.SamplePoint(p, lambda, time);
            return properties.sigma_a + properties.sigma_s;
        }

        public override void ExtinctionMinMax(Bounds3f region, SampledWavelengths lambda, out SampledSpectrum minimum,
                                              out SampledSpectrum maximum)
        {
            // The majorant of the medium is one constant, and the minimum is not used by the tracking
            maximum = medium.Majorant(lambda);
            minimum = new SampledSpectrum(0.0f);
        }

        public override PhaseFunction GetPhaseFunction(Point3f p, Float time, SampledWavelengths lambda)
        {
            return medium.SamplePoint(p, lambda, time).phase;
        }

        public override Bounds3f Bounds() { return medium.Bounds(); }

        public override bool IsEmissive() { return medium.IsEmissive(); }

        public RefractiveMedium Medium() { return medium; }

        private readonly RefractiveMedium medium;
    }

    // Whether a curve through a refractive volume met a surface of the scene inside the volume (the boundary of the volume, which the chord
    // of the last step meets, does not block it)
    public sealed class ChordOcclusion
    {
        public bool occluded;

        public static Func<Point3f, Point3f, Float> Chord(RefractiveMedium medium, Func<Point3f, Point3f, Float> sceneChord,
                                                          ChordOcclusion occlusion)
        {
            if (sceneChord == null)
            {
                return null;
            }
            Float diagonalOfVolume = Length(medium.Bounds().pMax - medium.Bounds().pMin);
            return (Point3f a, Point3f b) =>
            {
                Float fraction = sceneChord(a, b);
                if (fraction >= 0)
                {
                    Vector3f chord = b - a;
                    Point3f ahead = a + fraction * chord + 1e-3f * diagonalOfVolume * Normalize(chord);
                    if (medium.Contains(ahead))
                    {
                        occlusion.occluded = true;
                    }
                }
                return fraction;
            };
        }

        // The chords of a connection to a target inside the volume: the surface the target is on does not block the last chord
        public static Func<Point3f, Point3f, Float> SceneChord(Integrator integrator, RefractiveMedium medium, Float time, Point3f target)
        {
            Float nearTarget = 2e-3f * Length(medium.Bounds().pMax - medium.Bounds().pMin);
            return (Point3f a, Point3f b) =>
            {
                pstd.optional<ShapeIntersection> hit = integrator.Intersect(new Ray(a, b - a, time), 1);
                if (!hit || hit.value().tHit <= 1e-4f)
                {
                    return -1;
                }
                Point3f hitPoint = a + hit.value().tHit * (b - a);
                return Distance(hitPoint, target) < nearTarget ? -1 : hit.value().tHit;
            };
        }
    }

    // VolumeAggregate Definition (Fong listing 24): the intermediary between the integrator and the volumes. The integrator walks the
    // integration intervals of the iterator and never sees where the volumes end.
    public sealed unsafe class VolumeAggregate
    {
        // Iterator Definition: one integration interval, with the volumes that overlap it and the extinction bounds of their sum
        public sealed class Iterator
        {
            public Iterator(Float start, Float end, SampledSpectrum minimum, SampledSpectrum maximum, Volume[] volumes)
            {
                this.start = start;
                this.end = end;
                this.minimum = minimum;
                this.maximum = maximum;
                this.volumes = volumes;
            }

            public Float Start() { return start; }

            public Float End() { return end; }

            public SampledSpectrum MinExtinction() { return minimum; }

            public SampledSpectrum MaxExtinction() { return maximum; }

            public bool Finished() { return finished; }

            // operator++
            public void MoveNext()
            {
                // To do: the DDA walk to the next octree node; the mock has the one interval of the whole ray
                finished = true;
            }

            public int NumVolumes() { return volumes.Length; }

            public Volume[] Volumes() { return volumes; }

            private readonly Float start;
            private readonly Float end;
            private readonly SampledSpectrum minimum;
            private readonly SampledSpectrum maximum;
            private readonly Volume[] volumes;
            private bool finished;
        }

        public void Insert(Volume volume)
        {
            volumes.Add(volume);
            bounds = volumes.Count == 1 ? volume.Bounds() : Union(bounds, volume.Bounds());
        }

        // The integration intervals of a ray over [0, tMax]. To do: the octree of Fong section 6.8, each node with its overlapping
        // volumes and (min, max) of the extinction, subdivided while (max - min) * diagonal > T. Refractive volumes are not in it: they
        // own their region (their paths leave the ray).
        public Iterator GetIterator(Ray ray, Float tMax, SampledWavelengths lambda)
        {
            throw new NotImplementedException("VolumeAggregate.GetIterator");
        }

        public int Count() { return volumes.Count; }

        public Bounds3f Bounds() { return bounds; }

        private readonly List<Volume> volumes = new List<Volume>();
        private Bounds3f bounds;
    }

    // EventKind Definition: what SceneTracer.Trace found
    public enum EventKind
    {
        // A real scattering event at a surface (its BSDF) or in a volume (the phase function as a BSDF)
        Scatter = 0,

        // The ray left the scene
        Escaped = 1,

        // The path ended: absorbed in a volume, the weight went to zero, or the depth was reached
        Terminated = 2
    }

    // ScatterEdge Definition: how the edge that arrived at an event was traversed. A straight line unless a refractive volume curved it;
    // every geometry term and reverse density reads it, so refractive volumes need no special case in the renderers.
    public struct ScatterEdge
    {
        public bool curved;
        public Vector3f leaving;
        public Vector3f arriving;

        // dA_perp(arriving) / dOmega(leaving), and the same walked the other way (d^2 for a straight edge)
        public Float jacobian;
        public Float jacobianReverse;
    }

    // EmissionSample Definition: one emission found on the way to an event, with the state of the path where it was found (later
    // segments of the same Trace change the state). The renderer weights it by its own strategies.
    public struct EmissionSample
    {
        public bool isMedium;
        public Point3f p;
        public Vector3f direction;

        // The area light that was hit (null for a medium)
        public Light light;
        public int depth;

        // The emission was reached along a curved path of a refractive volume: no other strategy samples the direction it was
        // found along, so it counts in full
        public bool curved;

        // Surface: Le. Medium: sigma_a Le T_maj / (sigma_maj T_maj)[0], to be weighted by beta / r_e.Average().
        public SampledSpectrum radiance;
        public SampledSpectrum beta;
        public SampledSpectrum r_u;
        public SampledSpectrum r_l;
        public SampledSpectrum r_e;

        // (bdpt) What a camera subpath needs to make the collision a light vertex that ends it: the medium, the majorant and the
        // transmittance of the distance to the collision, the emission sigma_a Le, the null collisions of the segment so far, the
        // media the path is in, and the refractive medium whose curve the collision is on
        public Medium medium;
        public SampledSpectrum T_maj;
        public SampledSpectrum sigma_maj;
        public SampledSpectrum coefficient;
        public SampledSpectrum segU;
        public SampledSpectrum segL;
        public BoundaryStack stack;
        public RefractiveMedium curvedMedium;
    }

    // TraceBuffers Definition: the lists that one path reuses for every Trace (a tracer is shared by the threads, a path is not)
    public sealed class TraceBuffers
    {
        public List<EmissionSample> emission = new List<EmissionSample>();
        public List<VolumeCollision> passed = new List<VolumeCollision>();
    }

    // ScatterEvent Definition: the one thing a renderer sees. A surface and a medium point are both an Interaction with a BSDF.
    public struct ScatterEvent
    {
        public EventKind kind;
        public Interaction interaction;
        public BSDF bsdf;
        public bool inMedium;
        // The ray that ends the event came along a curved path, through this refractive medium (null if straight)
        public bool curved;
        public RefractiveMedium curvedMedium;
        // In a medium: the scattering coefficient at the point
        public SampledSpectrum sigma_s;
        public ScatterEdge edge;

        // The emission found on the way to the event, in the order of the path (buffers.emission)
        public List<EmissionSample> emission;
    }

    // SceneTracer Definition: what a renderer asks of the scene. It owns the boundary stack, the volume integration and the
    // replay of null collisions, the interface surfaces, the sampling of the next direction and the shadow rays, so none of them
    // appear in an integrator. Volpath and a bidirectional integrator are clients of the same calls.
    public abstract unsafe class SceneTracer
    {
        // The next event along ray. Updates the throughput and the spectral MIS factors in state (null collisions, the real
        // scatter, the end of a segment), crosses interface surfaces and boundaries, and leaves ray where the event is.
        public abstract ScatterEvent Trace(ref RayDifferential ray, ref PathState state, ref BoundaryStack stack, Sampler sampler,
                                           ref SampledWavelengths lambda, ScratchBuffer scratchBuffer, TraceBuffers buffers);

        // Samples the direction that leaves a Scatter event, updates the throughput and r_l, and moves ray (and the stack) along it
        public abstract bool Continue(ScatterEvent scatter, ref RayDifferential ray, ref PathState state, ref BoundaryStack stack,
                                      Sampler sampler, out BSDFSample sample);

        // The transmittance between two interactions through surfaces and volumes (today's Integrator.ShadowRay and TrSegments),
        // with the factors of the spectral MIS weights
        public abstract SampledSpectrum Transmittance(Interaction a, Interaction b, ref BoundaryStack stack,
                                                      SampledWavelengths lambda, out SampledSpectrum r_u,
                                                      out SampledSpectrum r_l);

        // A curved connection from a vertex inside a refractive volume (Volume.Connect of the volume on top of the stack)
public abstract bool Connect(Interaction vertex, Interaction target, ref BoundaryStack stack, SampledWavelengths lambda,
                                     ref RNG rng, out VolumeConnection connection);
    }

}  // namespace pbrt
