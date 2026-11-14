// (not in pbrt-v4) productionvolume.cs
//
// The volume integrators of J. Fong, M. Wrenninge, C. Kulla and R. Habel, "Production Volume Rendering" (SIGGRAPH 2017
// course), section 3, as the course writes them: a volume is a black box to the path tracer.
//
//     class Volume {
//         Volume(ShadingContext &ctx);
//         virtual bool Integrate(RendererServices &rs, const Ray &wi, Color &L, Color &transmittance, Color &weight,
//                                Point &P, Ray &wo, Geometry &g) = 0;
//         virtual Color Transmittance(RendererServices &rs, const Point &P0, const Point &P1) = 0;
//     };
//
// Integrate() is given the ray that enters (or continues in) the volume at the point of the shading context. It finds where
// the path goes next by itself (the nearest hit, through the renderer services) and returns that point with its geometry: the
// surface where the ray leaves, or a point of the volume where the light is scattered, whose "material" is the phase
// function. With them it returns the radiance L that it gathered on the way (what a single scattering volume does by itself),
// the transmittance of the beam and the weight of the sample. Transmittance() is for the shadow rays. The path tracer
// (cpu/productionpath.cs) knows of no collision, majorant or tracker: it asks the material of the surface that the ray is
// inside for its volume and calls these two functions.
//
// The integrators of the course, each for a homogeneous volume (closed forms) and a heterogeneous one (tracking):
//     BeersLawVolume, BeersLawHeterogeneousVolume                    absorption alone: what the medium scatters is not followed
//     SingleScatterHomogeneousVolume, SingleScatterHeterogeneousVolume    the light that is scattered once towards the eye:
//                                                                    a point of the segment is lit, and the beam goes on
//     MultiScatterHomogeneousVolume, MultiScatterHeterogeneousVolume      the path is scattered in the volume, any number of
//                                                                    times
// A medium chooses its integrator with "string scattering" "multiple" (default), "single" or "absorption"
// (VolumeIntegrator.Create), next to its "tracker" and its "transmittance" estimator, which the heterogeneous integrators use
// (the majorants are the medium's own, pbrt-v4's). The properties of a volume are those of a pbrt Medium; the volume that a ray
// is in comes from the material of the surface it entered (Material.GetVolume, the BoundaryStack with its priorities). Volumes
// that overlap (section 3.9) are one aggregate, the CompositeMedium of the stack, integrated by the member of the highest
// "overlappriority".
//
// Names: the course's RendererServices, ShadingContext and Geometry are VolumeServices, VolumeContext and VolumeGeometry here (the
// Open Shading Language port has classes of the first two names). The comment above keeps the course's.
//
// Colour. The course picks a channel to sample a distance with and weighs it against the others. Here the first wavelength
// of the path samples (pbrt-v4's hero wavelength), `weight` and `transmittance` are divided by its density, and the ratio of
// the density that each other wavelength would have had is multiplied into the `r` of the shading context; what a path
// contributes is divided by the mean of r over the wavelengths (the single-sample balance heuristic of pbrt-v4's volpath).
// The L returned by Integrate() is already divided so: the path tracer adds throughput * L.

using System;

namespace pbrt
{
    using static pbrt;

    public enum VolumeScattering
    {
        Multiple,
        Single,
        Absorption
    }

    // VolumeContext Definition: the point being shaded and the state of the path there
    public sealed unsafe class VolumeContext
    {
        public Point3f P;
        public Float time;
        public SampledWavelengths lambda;
        // The densities of the path for each wavelength, relative to that of the wavelength that sampled it
        public SampledSpectrum r = new SampledSpectrum(1f);
        // The medium that the ray is in at P (of the material that the path entered)
        public Medium medium;
        // Whether the path may still be scattered (its depth): a volume that gathers the light of a scattering by itself asks
        public bool mayScatter = true;

        public Point3f GetP() { return P; }
        public void SetP(Point3f p) { P = p; }
    }

    // VolumeGeometry Definition: what Integrate() and GetNearestHit() found. A surface, or a point of a volume with its phase function
    public sealed unsafe class VolumeGeometry
    {
        public bool isSurface;
        public ShapeIntersection surface;
        public MediumInteraction scatter;
        public PhaseFunction phase;
    }

    // VolumeServices Definition
    public abstract unsafe class VolumeServices
    {
        // The nearest surface along the ray (the direction of the ray has unit length); false if the ray leaves the scene
        public abstract bool GetNearestHit(Ray ray, out Point3f P, out Float distance, out VolumeGeometry g);

        public abstract Float GenerateRandomNumber();

        // A sample of the lights for the point of the context: its direction, its radiance over its density (with the weight
        // of the heuristic that combines it with a direction sampled from the phase function, whose density for wi is asked
        // through `phase`), and the transmittance of the beam from the light, with the factors of its r in `beamR`
        public abstract bool GenerateLightSample(VolumeContext ctx, MediumInteraction point, PhaseFunction phase, out Vector3f wi, out SampledSpectrum L,
                                                 out SampledSpectrum beamTransmittance, out SampledSpectrum beamR);

        public RNG rng;
    }

    // Volume Definition
    public abstract unsafe class Volume
    {
        public Volume(VolumeContext ctx, Medium medium)
        {
            m_ctx = ctx;
            this.medium = medium;
        }

        public abstract bool Integrate(VolumeServices rs, Ray wi, out SampledSpectrum L, out SampledSpectrum transmittance, out SampledSpectrum weight,
                                       out Point3f P, out Ray wo, out VolumeGeometry g);

        public abstract SampledSpectrum Transmittance(VolumeServices rs, Point3f P0, Point3f P1);

        protected VolumeContext m_ctx;
        protected Medium medium;

        // exp(-sigma d) by wavelength, for a distance that may be infinite
        protected static SampledSpectrum Attenuation(SampledSpectrum sigma, Float distance)
        {
            SampledSpectrum T = new SampledSpectrum(0f);
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                T[i] = sigma[i] == 0 ? 1 : (IsInf(distance) ? 0 : std.exp(-sigma[i] * distance));
            }
            return T;
        }

        // The ratio-tracking estimate of the transmittance along the ray up to tMax, for the extinction of the medium or, with
        // `absorptionOnly`, for its absorption alone; the densities go into r. With the estimator that the medium names.
        protected SampledSpectrum Track(VolumeServices rs, Ray ray, Float tMax, bool absorptionOnly, ref SampledSpectrum r)
        {
            SampledWavelengths lambda = m_ctx.lambda;
            SampledSpectrum T_ray = new SampledSpectrum(1f), rl = new SampledSpectrum(1f), ru = new SampledSpectrum(1f);
            TransmittanceEstimator estimator = medium.volumeIntegrator.transmittance;
            if (!absorptionOnly)
            {
                SampledSpectrum T_closed;
                if (estimator.TryClosedForm(ray, medium, tMax, lambda, out T_closed))
                {
                    return T_closed;
                }
                if (estimator.TryEstimate(ray, medium, tMax, ref rs.rng, lambda, ref T_ray, ref rl, ref ru))
                {
                    r *= rl;
                    return T_ray;
                }
            }
            RNG rng = rs.rng;
            SampledSpectrum T_maj = medium.volumeIntegrator.tracker.SampleT_maj(ray, medium, tMax, rs.GenerateRandomNumber(), ref rng, lambda,
                (Point3f p, MediumProperties mp, SampledSpectrum sigma_maj, SampledSpectrum T_maj_) =>
                {
                    SampledSpectrum sigma_n = absorptionOnly ? ClampZero(sigma_maj - mp.sigma_a) : ClampZero(sigma_maj - mp.sigma_a - mp.sigma_s);
                    if (absorptionOnly)
                    {
                        MediumEvents.RatioTrackingStep(ref T_ray, ref rl, ref ru, T_maj_, sigma_maj, sigma_n);
                    }
                    else
                    {
                        estimator.Step(ref T_ray, ref rl, ref ru, T_maj_, sigma_maj, sigma_n, ref rng);
                    }
                    return (bool)T_ray;
                });
            rs.rng = rng;
            if (!T_ray)
            {
                return T_ray;
            }
            if (absorptionOnly)
            {
                MediumEvents.FinishSegment(ref T_ray, ref rl, ref ru, T_maj);
            }
            else
            {
                estimator.Finish(ref T_ray, ref rl, ref ru, T_maj);
            }
            r *= rl;
            return T_ray;
        }

        protected static Ray Between(Point3f P0, Point3f P1, Float time, out Float distance)
        {
            Vector3f d = P1 - P0;
            distance = Length(d);
            return new Ray(P0, distance > 0 ? d / distance : new Vector3f(0, 0, 1), time);
        }

        // The emission along the homogeneous segment of the length given: int_0^d exp(-sigma_t t) sigma_a Le dt
        protected static SampledSpectrum Emitted(MediumProperties mp, SampledSpectrum sigma_t, Float distance)
        {
            SampledSpectrum result = new SampledSpectrum(0f);
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                if (mp.Le[i] == 0 || mp.sigma_a[i] == 0)
                {
                    continue;
                }
                Float through = sigma_t[i] == 0 ? distance : (IsInf(distance) ? 1 / sigma_t[i] : (1 - std.exp(-sigma_t[i] * distance)) / sigma_t[i]);
                result[i] = mp.sigma_a[i] * mp.Le[i] * through;
            }
            return result;
        }

        // The factory: the integrator that the medium asks for
        public static Volume Create(VolumeContext ctx, Medium medium)
        {
            bool homogeneous = medium is HomogeneousMedium;
            switch (medium.volumeIntegrator.scattering)
            {
            case VolumeScattering.Absorption:
                return homogeneous ? new BeersLawVolume(ctx, medium) : new BeersLawHeterogeneousVolume(ctx, medium);
            case VolumeScattering.Single:
                return homogeneous ? new SingleScatterHomogeneousVolume(ctx, medium) : new SingleScatterHeterogeneousVolume(ctx, medium);
            default:
                return homogeneous ? new MultiScatterHomogeneousVolume(ctx, medium) : new MultiScatterHeterogeneousVolume(ctx, medium);
            }
        }
    }

    // BeersLawVolume Definition (the course, listing "Beer's law")
    public sealed unsafe class BeersLawVolume : Volume
    {
        public BeersLawVolume(VolumeContext ctx, Medium medium) : base(ctx, medium) { }

        public override bool Integrate(VolumeServices rs, Ray wi, out SampledSpectrum L, out SampledSpectrum transmittance, out SampledSpectrum weight,
                                       out Point3f P, out Ray wo, out VolumeGeometry g)
        {
            MediumProperties mp = medium.SamplePoint(m_ctx.P, m_ctx.lambda, wi.time);
            Float distance;
            bool hit = rs.GetNearestHit(wi, out P, out distance, out g);
            transmittance = Attenuation(mp.sigma_a, distance);
            L = Emitted(mp, mp.sigma_a, distance) / m_ctx.r.Average();
            weight = new SampledSpectrum(1f);
            wo = new Ray(P, wi.d, wi.time);
            return hit;
        }

        public override SampledSpectrum Transmittance(VolumeServices rs, Point3f P0, Point3f P1)
        {
            MediumProperties mp = medium.SamplePoint(P0, m_ctx.lambda, m_ctx.time);
            return Attenuation(mp.sigma_a, Distance(P0, P1));
        }
    }

    // BeersLawHeterogeneousVolume Definition
    public sealed unsafe class BeersLawHeterogeneousVolume : Volume
    {
        public BeersLawHeterogeneousVolume(VolumeContext ctx, Medium medium) : base(ctx, medium) { }

        public override bool Integrate(VolumeServices rs, Ray wi, out SampledSpectrum L, out SampledSpectrum transmittance, out SampledSpectrum weight,
                                       out Point3f P, out Ray wo, out VolumeGeometry g)
        {
            Float distance;
            bool hit = rs.GetNearestHit(wi, out P, out distance, out g);
            L = new SampledSpectrum(0f);
            weight = new SampledSpectrum(1f);
            SampledSpectrum r = m_ctx.r;
            // the emission, at the collisions of one walk; the transmittance, by another
            if (medium.IsEmissive())
            {
                SampledSpectrum T = new SampledSpectrum(1f), re = m_ctx.r, sum = new SampledSpectrum(0f);
                RNG rng = rs.rng;
                SampledWavelengths lambda = m_ctx.lambda;
                medium.volumeIntegrator.tracker.SampleT_maj(wi, medium, distance, rs.GenerateRandomNumber(), ref rng, lambda,
                    (Point3f p, MediumProperties mp, SampledSpectrum sigma_maj, SampledSpectrum T_maj) =>
                    {
                        Float pdf = sigma_maj[0] * T_maj[0];
                        SampledSpectrum numerator = T * T_maj / pdf, density = re * T_maj * sigma_maj / pdf;
                        if ((bool)density && (bool)mp.Le)
                        {
                            sum += numerator * mp.sigma_a * mp.Le / density.Average();
                        }
                        SampledSpectrum sigma_n = ClampZero(sigma_maj - mp.sigma_a);
                        Float pdfNull = T_maj[0] * sigma_maj[0];
                        T *= T_maj * sigma_n / pdfNull;
                        re *= T_maj * sigma_maj / pdfNull;
                        return (bool)T;
                    });
                rs.rng = rng;
                L = sum;
            }
            transmittance = Track(rs, wi, distance, true, ref r);
            m_ctx.r = r;
            wo = new Ray(P, wi.d, wi.time);
            return hit;
        }

        public override SampledSpectrum Transmittance(VolumeServices rs, Point3f P0, Point3f P1)
        {
            Float distance;
            Ray ray = Between(P0, P1, m_ctx.time, out distance);
            SampledSpectrum r = m_ctx.r;
            SampledSpectrum T = Track(rs, ray, distance, true, ref r);
            m_ctx.r = r;
            return T;
        }
    }

    // SingleScatterHomogeneousVolume Definition (the course, listing "single scattering in a homogeneous volume"): a point of
    // the segment is chosen with the density of the transmittance, normalised over the segment, and lit; the beam itself goes
    // on to where the ray leaves, with the transmittance of the segment
    public sealed unsafe class SingleScatterHomogeneousVolume : Volume
    {
        public SingleScatterHomogeneousVolume(VolumeContext ctx, Medium medium) : base(ctx, medium) { }

        public override bool Integrate(VolumeServices rs, Ray wi, out SampledSpectrum L, out SampledSpectrum transmittance, out SampledSpectrum weight,
                                       out Point3f P, out Ray wo, out VolumeGeometry g)
        {
            MediumProperties mp = medium.SamplePoint(m_ctx.P, m_ctx.lambda, wi.time);
            SampledSpectrum sigma_t = mp.sigma_a + mp.sigma_s;
            Float distance;
            bool hit = rs.GetNearestHit(wi, out P, out distance, out g);
            transmittance = Attenuation(sigma_t, distance);
            weight = new SampledSpectrum(1f);
            wo = new Ray(P, wi.d, wi.time);
            L = Emitted(mp, sigma_t, distance) / m_ctx.r.Average();
            if (!(bool)mp.sigma_s || !(distance > 0) || !m_ctx.mayScatter)
            {
                return hit;
            }
            // The distance of the scattering: sigma exp(-sigma t) / (1 - exp(-sigma d)) for the first wavelength
            Float xi = rs.GenerateRandomNumber();
            Float s0 = sigma_t[0];
            Float t;
            if (s0 == 0)
            {
                if (IsInf(distance))
                {
                    return hit;
                }
                t = xi * distance;
            }
            else
            {
                Float span = IsInf(distance) ? 1 : 1 - std.exp(-s0 * distance);
                t = -std.log(1 - xi * span) / s0;
            }
            if (!(t < distance) || IsInf(t))
            {
                return hit;
            }
            SampledSpectrum density = new SampledSpectrum(0f);
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                Float s = sigma_t[i];
                if (s == 0)
                {
                    density[i] = IsInf(distance) ? 0 : 1 / distance;
                }
                else
                {
                    density[i] = s * std.exp(-s * t) / (IsInf(distance) ? 1 : 1 - std.exp(-s * distance));
                }
            }
            if (!(density[0] > 0))
            {
                return hit;
            }
            Point3f point = wi.Apply(t);
            MediumInteraction intr = new MediumInteraction(point, -wi.d, wi.time, medium, mp.phase);
            Point3f saved = m_ctx.P;
            m_ctx.SetP(point);
            Vector3f direction;
            SampledSpectrum light, beam, beamR;
            if (rs.GenerateLightSample(m_ctx, intr, mp.phase, out direction, out light, out beam, out beamR))
            {
                SampledSpectrum numerator = mp.sigma_s * Attenuation(sigma_t, t) * mp.phase.p(-wi.d, direction) * light * beam / density[0];
                SampledSpectrum r = m_ctx.r * density / density[0] * beamR;
                if ((bool)r)
                {
                    L += numerator / r.Average();
                }
            }
            m_ctx.SetP(saved);
            return hit;
        }

        public override SampledSpectrum Transmittance(VolumeServices rs, Point3f P0, Point3f P1)
        {
            MediumProperties mp = medium.SamplePoint(P0, m_ctx.lambda, m_ctx.time);
            return Attenuation(mp.sigma_a + mp.sigma_s, Distance(P0, P1));
        }
    }

    // SingleScatterHeterogeneousVolume Definition: the point is the first real collision of a delta-tracking walk (there may
    // be none), and the transmittance of the beam is a separate estimate
    public sealed unsafe class SingleScatterHeterogeneousVolume : Volume
    {
        public SingleScatterHeterogeneousVolume(VolumeContext ctx, Medium medium) : base(ctx, medium) { }

        public override bool Integrate(VolumeServices rs, Ray wi, out SampledSpectrum L, out SampledSpectrum transmittance, out SampledSpectrum weight,
                                       out Point3f P, out Ray wo, out VolumeGeometry g)
        {
            Float distance;
            bool hit = rs.GetNearestHit(wi, out P, out distance, out g);
            weight = new SampledSpectrum(1f);
            wo = new Ray(P, wi.d, wi.time);
            SampledWavelengths lambda = m_ctx.lambda;
            SampledSpectrum numerator = new SampledSpectrum(1f), density = m_ctx.r, emitted = new SampledSpectrum(0f);
            bool found = false;
            Point3f point = default;
            MediumProperties at = default;
            RNG rng = rs.rng;
            medium.volumeIntegrator.tracker.SampleT_maj(wi, medium, distance, rs.GenerateRandomNumber(), ref rng, lambda,
                (Point3f p, MediumProperties mp, SampledSpectrum sigma_maj, SampledSpectrum T_maj) =>
                {
                    Float pdfCollision = sigma_maj[0] * T_maj[0];
                    if ((bool)mp.Le)
                    {
                        SampledSpectrum de = density * T_maj * sigma_maj / pdfCollision;
                        if ((bool)de)
                        {
                            emitted += numerator * T_maj / pdfCollision * mp.sigma_a * mp.Le / de.Average();
                        }
                    }
                    SampledSpectrum sigma_t = mp.sigma_a + mp.sigma_s;
                    Float pReal = std.min<Float>(1, sigma_t[0] / sigma_maj[0]);
                    if (rng.Uniform<Float>() < pReal)
                    {
                        Float pdf = T_maj[0] * sigma_t[0];
                        numerator *= T_maj * mp.sigma_s / pdf;
                        density *= T_maj * sigma_t / pdf;
                        found = true;
                        point = p;
                        at = mp;
                        return false;
                    }
                    SampledSpectrum sigma_n = ClampZero(sigma_maj - sigma_t);
                    Float pdfNull = T_maj[0] * sigma_n[0];
                    numerator *= T_maj * sigma_n / pdfNull;
                    density *= T_maj * sigma_n / pdfNull;
                    return (bool)numerator && (bool)density;
                });
            rs.rng = rng;
            L = emitted;
            if (found && m_ctx.mayScatter && (bool)numerator && (bool)density)
            {
                MediumInteraction intr = new MediumInteraction(point, -wi.d, wi.time, medium, at.phase);
                Point3f saved = m_ctx.P;
                m_ctx.SetP(point);
                Vector3f direction;
                SampledSpectrum light, beam, beamR;
                if (rs.GenerateLightSample(m_ctx, intr, at.phase, out direction, out light, out beam, out beamR))
                {
                    SampledSpectrum r = density * beamR;
                    if ((bool)r)
                    {
                        L += numerator * at.phase.p(-wi.d, direction) * light * beam / r.Average();
                    }
                }
                m_ctx.SetP(saved);
            }
            SampledSpectrum rBeam = m_ctx.r;
            transmittance = Track(rs, wi, distance, false, ref rBeam);
            m_ctx.r = rBeam;
            return hit;
        }

        public override SampledSpectrum Transmittance(VolumeServices rs, Point3f P0, Point3f P1)
        {
            Float distance;
            Ray ray = Between(P0, P1, m_ctx.time, out distance);
            SampledSpectrum r = m_ctx.r;
            SampledSpectrum T = Track(rs, ray, distance, false, ref r);
            m_ctx.r = r;
            return T;
        }
    }

    // MultiScatterHomogeneousVolume Definition (the course, listing "multiple scattering in a homogeneous volume"): the
    // distance to the next collision is sampled; before the surface, the path is scattered there (the weight is the albedo,
    // and the geometry returned is the point with its phase function); beyond it, the ray reaches the surface
    public sealed unsafe class MultiScatterHomogeneousVolume : Volume
    {
        public MultiScatterHomogeneousVolume(VolumeContext ctx, Medium medium) : base(ctx, medium) { }

        public override bool Integrate(VolumeServices rs, Ray wi, out SampledSpectrum L, out SampledSpectrum transmittance, out SampledSpectrum weight,
                                       out Point3f P, out Ray wo, out VolumeGeometry g)
        {
            MediumProperties mp = medium.SamplePoint(m_ctx.P, m_ctx.lambda, wi.time);
            SampledSpectrum sigma_t = mp.sigma_a + mp.sigma_s;
            Float distance;
            bool hit = rs.GetNearestHit(wi, out P, out distance, out g);
            transmittance = new SampledSpectrum(1f);
            L = new SampledSpectrum(0f);
            wo = new Ray(P, wi.d, wi.time);
            Float s0 = sigma_t[0];
            Float t = s0 > 0 ? -std.log(1 - rs.GenerateRandomNumber()) / s0 : Infinity;
            if (t < distance)
            {
                // a collision: exp(-sigma t) sigma_s over the density sigma_0 exp(-sigma_0 t) of the first wavelength
                SampledSpectrum T = Attenuation(sigma_t, t);
                Float pdf = s0 * T[0];
                SampledSpectrum density = m_ctx.r * sigma_t * T / pdf;
                if ((bool)mp.Le && (bool)density)
                {
                    L = T * mp.sigma_a * mp.Le / pdf / density.Average();
                }
                weight = T * mp.sigma_s / pdf;
                m_ctx.r = density;
                P = wi.Apply(t);
                g = new VolumeGeometry();
                g.isSurface = false;
                g.phase = mp.phase;
                g.scatter = new MediumInteraction(P, -wi.d, wi.time, medium, mp.phase);
                wo = new Ray(P, wi.d, wi.time);
                return true;
            }
            // no collision before the surface: exp(-sigma d) over the probability exp(-sigma_0 d)
            SampledSpectrum through = Attenuation(sigma_t, distance);
            if (!(through[0] > 0))
            {
                weight = new SampledSpectrum(0f);
                return false;
            }
            weight = through / through[0];
            m_ctx.r *= through / through[0];
            return hit;
        }

        public override SampledSpectrum Transmittance(VolumeServices rs, Point3f P0, Point3f P1)
        {
            MediumProperties mp = medium.SamplePoint(P0, m_ctx.lambda, m_ctx.time);
            return Attenuation(mp.sigma_a + mp.sigma_s, Distance(P0, P1));
        }
    }

    // MultiScatterHeterogeneousVolume Definition: delta tracking through the majorants of the medium, with its tracker. A
    // collision is an absorption (the path ends), a scattering or a null collision, as in pbrt-v4.
    public sealed unsafe class MultiScatterHeterogeneousVolume : Volume
    {
        public MultiScatterHeterogeneousVolume(VolumeContext ctx, Medium medium) : base(ctx, medium) { }

        public override bool Integrate(VolumeServices rs, Ray wi, out SampledSpectrum L, out SampledSpectrum transmittance, out SampledSpectrum weight,
                                       out Point3f P, out Ray wo, out VolumeGeometry g)
        {
            Float distance;
            Point3f exit;
            VolumeGeometry surface;
            bool hit = rs.GetNearestHit(wi, out exit, out distance, out surface);
            transmittance = new SampledSpectrum(1f);
            SampledWavelengths lambda = m_ctx.lambda;
            SampledSpectrum beta = new SampledSpectrum(1f), r = m_ctx.r, emitted = new SampledSpectrum(0f);
            bool scattered = false, absorbed = false;
            Point3f point = default;
            MediumProperties at = default;
            RNG rng = rs.rng;
            SampledSpectrum T_end = medium.volumeIntegrator.tracker.SampleT_maj(wi, medium, distance, rs.GenerateRandomNumber(), ref rng, lambda,
                (Point3f p, MediumProperties mp, SampledSpectrum sigma_maj, SampledSpectrum T_maj) =>
                {
                    if ((bool)mp.Le)
                    {
                        Float pdfCollision = sigma_maj[0] * T_maj[0];
                        SampledSpectrum de = r * T_maj * sigma_maj / pdfCollision;
                        if ((bool)de)
                        {
                            emitted += beta * T_maj / pdfCollision * mp.sigma_a * mp.Le / de.Average();
                        }
                    }
                    MediumEventType evt = MediumEvents.SampleEvent(mp, sigma_maj, rng.Uniform<Float>());
                    if (evt == MediumEventType.Absorb)
                    {
                        absorbed = true;
                        return false;
                    }
                    if (evt == MediumEventType.Scatter)
                    {
                        Float pdf = T_maj[0] * mp.sigma_s[0];
                        beta *= T_maj * mp.sigma_s / pdf;
                        r *= T_maj * mp.sigma_s / pdf;
                        scattered = true;
                        point = p;
                        at = mp;
                        return false;
                    }
                    SampledSpectrum sigma_n = ClampZero(sigma_maj - mp.sigma_a - mp.sigma_s);
                    Float pdfNull = T_maj[0] * sigma_n[0];
                    beta *= T_maj * sigma_n / pdfNull;
                    r *= T_maj * sigma_n / pdfNull;
                    return (bool)beta && (bool)r;
                });
            rs.rng = rng;
            L = emitted;
            P = exit;
            g = surface;
            wo = new Ray(exit, wi.d, wi.time);
            if (absorbed || !(bool)beta || !(bool)r)
            {
                weight = new SampledSpectrum(0f);
                return false;
            }
            if (scattered)
            {
                weight = beta;
                m_ctx.r = r;
                P = point;
                g = new VolumeGeometry();
                g.isSurface = false;
                g.phase = at.phase;
                g.scatter = new MediumInteraction(point, -wi.d, wi.time, medium, at.phase);
                wo = new Ray(point, wi.d, wi.time);
                return true;
            }
            if (!(T_end[0] > 0))
            {
                weight = new SampledSpectrum(0f);
                return false;
            }
            weight = beta * T_end / T_end[0];
            m_ctx.r = r * T_end / T_end[0];
            return hit;
        }

        public override SampledSpectrum Transmittance(VolumeServices rs, Point3f P0, Point3f P1)
        {
            Float distance;
            Ray ray = Between(P0, P1, m_ctx.time, out distance);
            SampledSpectrum r = m_ctx.r;
            SampledSpectrum T = Track(rs, ray, distance, false, ref r);
            m_ctx.r = r;
            return T;
        }
    }
}
