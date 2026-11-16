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
//     SingleScatterPDFVolume (listing 13), EmptyVolume (listing 2), AggregateVolume (section 6, volumeaggregate.cs)
// A medium chooses its integrator with "string scattering" "multiple" (default), "single", "singlepdf", "absorption" or "none"
// (VolumeIntegrator.Create), next to its "tracker" and its "transmittance" estimator, which the heterogeneous integrators use
// (the majorants are the medium's own, pbrt-v4's). The properties of a volume are those of a pbrt Medium; the volume that a ray
// is in comes from the material of the surface it entered (Material.GetVolume, the BoundaryStack with its priorities). Volumes
// that overlap (section 3.9) are one aggregate, the CompositeMedium of the stack, integrated by the member of the highest
// "overlappriority".
//
// Section 4, in the single scattering volumes (all of it by parameters of the medium, the defaults being what was before):
//     (the same parameter on a multiple scattering volume: equiangular sampling beside the next-event estimation of the
//     path, Miller et al. 2019; Volume.EquiangularShare)
//     "string distancesampling" "density" | "equiangular" | "mis"    where along the ray the light is gathered (4.2):
//                                    EquiangularDensity about a point of a light that the renderer gives (ChooseLight)
//     "string lightsampling" "light" | "mis"     the light at that point from the lights, or from the lights and the phase
//                                    function (Volume.DirectLighting; EvaluateLightSample)
//     "integer lightsamples"         the number of points lit along one interval (4.1)
//     "string doublescattering" "none" | "plain" | "joint" | "mis"     the light scattered twice as well, the second by the joint
//                                    importance sampling of Georgiev et al. 2013 (Volume.DoubleScattering)
// Section 3.8: Volume.Emission(), what a volume emits between two points, which the direction sampled from the phase
// function gathers on its way. Section 3.7: "float contrast", in the shadow rays of the multiple scattering volumes.
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
        Absorption,
        // (the course, listing 13: the scattering point by a density built along the ray, the transmittance by quadrature)
        SinglePDF,
        // (the course, listing 2: the volume does nothing)
        None
    }

    // How a single scattering volume chooses its point along the ray (the course, section 4.2)
    public enum VolumeDistanceSampling
    {
        // with the density of the transmittance (tracking), or the density that the march builds
        Density,
        // with the equiangular density about a point of a light (Kulla and Fajardo 2012)
        Equiangular,
        // with both, weighed by the heuristic
        MIS
    }

    // Whether a multiple scattering volume also samples, at each of its collisions, one more scattering on the way to a light
    // at a position, and how (Volume.OnceMoreShare, Volume.OnceMoreShare2)
    public enum VolumeOnceMore
    {
        None,
        // Hanika, Weidlich and Droske 2022 as published: the scattering angle from the phase function, forward only
        OnceMore,
        // the same over all angles, with candidates resampled by what the first leaves in the weight
        OnceMore2
    }

    // The density of once-more scattered next-event estimation, and what its two implementations share.
    //
    // From a point c towards a point a of a light at the distance s, one scattering is put in between, at b. With theta the
    // angle by which the light is deflected at b and alpha the angle at c between the directions of a and of b, b is on the
    // arc of the circle through c and a of the points that see the two under pi - theta (the inscribed angle), at
    //     |cb| = s sin(theta - alpha) / sin(theta),     |ba| = s sin(alpha) / sin(theta),     0 < alpha < theta,
    // turned by phi about the axis from c to a. In (theta, alpha, phi) the volume is |cb|^2 sin(alpha) s sin(alpha) /
    // sin^2(theta), which with the two geometry terms 1 / (|cb|^2 |ba|^2) leaves 1 / s: the product of the two inverse squared
    // distances is constant in alpha and phi. (The paper's fractional distance t along the axis, equation 41, is
    // |cb| cos(alpha) / s with alpha uniform.) So theta is drawn from the phase function, as a direction is, and alpha and phi
    // uniformly; the density of b per solid angle about c and per length along that direction is
    //     p(theta) / (2 pi theta)  s / |ba|^2,
    // with p(theta) the density of the angle: 2 pi sin(theta) times the phase function, for theta below pi / 2
    // ("omnee"); or, over all angles, three quarters of that and a quarter uniform in theta ("omnee2": the weight
    // theta / sin(theta) that the first leaves is not bounded towards pi).
    public static unsafe class OnceMoreScattering
    {
        // (the part of the angles of "omnee2" that is drawn uniformly)
        public const Float Uniform = 0.25f;

        // The direction that a direction is deflected to by an angle (about any axis: the phase function only knows the angle)
        public static Vector3f Deflected(Vector3f from, Float cos, Float sin)
        {
            Vector3f v1, v2;
            CoordinateSystem(from, &v1, &v2);
            return cos * from + sin * v1;
        }

        // The density of the angle theta, with `phase` the phase function that it is drawn from and `wo` what it is given
        public static Float Angle(VolumeOnceMore mode, PhaseFunction phase, Vector3f wo, Float theta)
        {
            if (mode == VolumeOnceMore.None || !(theta > 0) || !(theta < Pi))
            {
                return 0;
            }
            Float sin = std.sin(theta), cos = std.cos(theta);
            Float fromPhase = 2 * Pi * sin * phase.PDF(wo, Deflected(-wo, cos, sin));
            if (mode == VolumeOnceMore.OnceMore)
            {
                return theta < Pi / 2 ? fromPhase : 0;
            }
            return (1 - Uniform) * fromPhase + Uniform / Pi;
        }

        // The density of b, per solid angle about c and per length, for a light at a
        public static Float Density(VolumeOnceMore mode, PhaseFunction phase, Vector3f wo, Point3f c, Point3f b, Point3f a)
        {
            if (mode == VolumeOnceMore.None)
            {
                return 0;
            }
            Float s = Distance(c, a), first = Distance(c, b), second = Distance(b, a);
            if (!(s > 0) || !(first > 0) || !(second > 0))
            {
                return 0;
            }
            Float theta = SafeACos(Dot((b - c) / first, (a - b) / second));
            Float angle = Angle(mode, phase, wo, theta);
            return angle > 0 ? angle / (2 * Pi * theta) * s / (second * second) : 0;
        }
    }

    // Whether a single scattering volume also gathers the light that is scattered twice, and how it samples it
    // (Volume.DoubleScattering)
    public enum VolumeDoubleScattering
    {
        None,
        // as a path would: the direction from the phase function, the distance from the extinction
        Plain,
        // the joint importance sampling of Georgiev et al. 2013, about a point of the light
        Joint,
        // one of the two by chance, weighed by the densities of both
        MIS
    }

    // A light that the renderer chose for a ray, with the probability of the choice and, if it has a position, a point of it
    public sealed class VolumeLight
    {
        public Light light;
        public Float probability;
        public bool hasPoint;
        public Point3f point;
        // The light is at a position: the point is the only one it has
        public bool fixedPoint;
        // For a point of a curved path (RefractiveVolume): `point` is where the curve puts the point of the light, which
        // is at `worldPoint` in the scene
        public bool onCurve;
        public Point3f worldPoint;
    }

    // Where a connection ended: the volume that the point is in, and the stack of the volumes around it for what the renderer
    // traces from there
    public sealed class VolumePlace
    {
        public Medium medium;
        public BoundaryStack stack;
    }

    // The density in proportion to the inverse distance to a point, along a ray (Georgiev et al. 2013; Novak et al. 2018,
    // equations 58 and 61): for a point at the distance h from the ray, whose foot on the ray is at t0,
    //     pdf(t) = 1 / (C sqrt(h^2 + (t - t0)^2)),   t = t0 + h sinh(A0 + xi C),   A = asinh((t - t0) / h),   C = A1 - A0
    // over [tMin, tMax]. Not for a ray without end (the integral does not converge), nor for a point on the ray.
    public struct InverseDistanceDensity
    {
        public bool valid;

        public static InverseDistanceDensity Create(Ray ray, Float tMin, Float tMax, Point3f point)
        {
            InverseDistanceDensity density = default;
            if (IsInf(tMax) || !(tMax > tMin))
            {
                return density;
            }
            Vector3f to = point - ray.o;
            density.foot = Dot(to, ray.d);
            density.h = Length(to - (Float)density.foot * ray.d);
            if (!(density.h > 1e-6 * Length(to)))
            {
                return density;
            }
            density.tMin = tMin;
            density.tMax = tMax;
            density.first = Math.Asinh((tMin - density.foot) / density.h);
            density.extent = Math.Asinh((tMax - density.foot) / density.h) - density.first;
            density.valid = density.extent > 0;
            return density;
        }

        public Float Sample(Float xi)
        {
            return (Float)(foot + h * Math.Sinh(first + xi * extent));
        }

        public Float PDF(Float t)
        {
            if (!valid || !(t >= tMin) || !(t <= tMax))
            {
                return 0;
            }
            double along = t - foot;
            return (Float)(1 / (extent * Math.Sqrt(h * h + along * along)));
        }

        private double foot, h, first, extent;
        private Float tMin, tMax;
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
        // Whether it may be scattered twice more (a volume that gathers the double scattering by itself asks)
        public bool mayScatterTwice = true;
        // The number of scatterings of the path so far (the contrast control of section 3.7 asks)
        public int depth;
        // The throughput of the path up to the point (spectral tracking sets its probabilities by it)
        public SampledSpectrum throughput = new SampledSpectrum(1f);
        // Set by a volume whose light paths are not straight (RefractiveVolume) when Integrate has bent the ray: the ray as
        // it arrives at the point that Integrate returns (or as it leaves the scene), in the place of the ray it was given
        public bool bent;
        public Ray arrival;
        // For the search of the path between two points through a volume that bends rays (the path tracer's
        // CurvedConnection): a plane that stops the ray that Transmit follows (`atPlane` is then set, and Transmit's P is the
        // point of the plane), and whether only the geometry is wanted (no transmittance is estimated)
        public bool stopAtPlane;
        public Point3f stopPoint;
        public Vector3f stopNormal;
        public bool atPlane;
        public bool geometryOnly;
        // Set by a volume that bends the ray for the volume that it walks the curve with: the collision that the ray
        // leaves, in the scene (in the place of `joint`, whose points would be those of the straight ray)
        public JointOnCurve jointOnCurve;
        // Two small changes of the ray that is followed (of its point and of its direction), which a volume that bends
        // the ray carries to where it leaves the ray (CurvedPath.Carry): for the search of the path to a light
        public bool differentials;
        public Vector3f offset1, turn1, offset2, turn2;
        // Set by the path tracer's shadow ray when a volume bent it
        public bool shadowBent;
        // A path tracer that carries the polarisation of the light: the volumes gather no light of their own from the lights
        // (the equiangular point, the points drawn jointly), which they would add without its polarisation
        public bool polarised;
        // Set with `bent`: the direction that the ray started in and the one that it arrives in, each with an axis across
        // it; the second axis is the first carried along the curve without turning about it (the polarisation of light in
        // a medium whose index varies slowly keeps its angle to such an axis)
        public Vector3f startDirection, startAxis, arrivalDirection, arrivalAxis;
        // What weighs the emission that the volume gathers along the ray that it is given (Volume.Integrate), where the path
        // tracer also samples the emission of the medium from the point that the ray left (forward next event estimation):
        // the two find the same integral along the ray, and share it by the densities of the direction
        public Float emissionWeight = 1;
        // The densities, relative to the wavelength that samples, of the path that reaches the first point of Volume.Emission()
        // where it is called through Volume.EmissionOver(); a volume that tracks the emission with that wavelength goes on
        // from them and says so
        public SampledSpectrum emissionDensity = new SampledSpectrum(1f);
        public bool emissionDensityUsed;
        // The collision that the ray given to Volume.Integrate() leaves from, where it was returned with VolumeGeometry.joint;
        // null for any other ray
        public VolumeJoint joint;

        public Point3f GetP() { return P; }
        public void SetP(Point3f p) { P = p; }
    }

    // EquiangularDensity Definition (Kulla and Fajardo 2012; the course, section 4.2.2). For a point light at the distance D
    // from the ray, whose foot on the ray is at delta, the geometric term along the ray is 1 / (D^2 + (t - delta)^2); the
    // density in proportion to it over [tMin, tMax] is
    //     pdf(t) = D / ((theta_b - theta_a) (D^2 + (t - delta)^2)),   t = delta + D tan((1 - xi) theta_a + xi theta_b),
    // with theta the angle that the point of the ray is seen under from the light, measured from the foot. For a light on the
    // line of the ray (D = 0) it is pdf(t') = a b / ((b - a) t'^2), t' = a b / (b + (a - b) xi), with t' the distance from the
    // light; a light inside the segment itself has no such density (its integral does not converge), and there is none then.
    public struct EquiangularDensity
    {
        public bool valid;

        public static EquiangularDensity Create(Ray ray, Float tMin, Float tMax, Point3f pivot)
        {
            EquiangularDensity e = default;
            Vector3f to = pivot - ray.o;
            e.delta = Dot(to, ray.d);
            e.D = Length(to - (Float)e.delta * ray.d);
            e.tMin = tMin;
            e.tMax = tMax;
            double a = tMin - e.delta, b = IsInf(tMax) ? double.PositiveInfinity : tMax - e.delta;
            if (!(b > a))
            {
                return e;
            }
            if (e.D > 1e-6 * Length(to))
            {
                e.thetaA = Math.Atan2(a, e.D);
                e.thetaB = double.IsInfinity(b) ? Math.PI / 2 : Math.Atan2(b, e.D);
                e.valid = e.thetaB > e.thetaA;
                return e;
            }
            e.onRay = true;
            if (a > 0)
            {
                e.sign = 1;
                e.a = a;
                e.b = b;
                e.valid = true;
            }
            else if (b < 0)
            {
                e.sign = -1;
                e.a = -b;
                e.b = -a;
                e.valid = true;
            }
            return e;
        }

        public Float Sample(Float xi)
        {
            if (!onRay)
            {
                return (Float)(delta + D * Math.Tan((1 - xi) * thetaA + xi * thetaB));
            }
            double distance = double.IsInfinity(b) ? a / (1 - xi) : a * b / (b + (a - b) * xi);
            return (Float)(delta + sign * distance);
        }

        public Float PDF(Float t)
        {
            if (!valid || !(t >= tMin) || !(t <= tMax))
            {
                return 0;
            }
            double along = t - delta;
            if (!onRay)
            {
                return (Float)(D / ((thetaB - thetaA) * (D * D + along * along)));
            }
            along *= sign;
            if (!(along > 0))
            {
                return 0;
            }
            return (Float)(double.IsInfinity(b) ? a / (along * along) : a * b / ((b - a) * along * along));
        }

        private double delta, D, thetaA, thetaB, a, b, sign;
        private Float tMin, tMax;
        private bool onRay;
    }

    // VolumeDirectWeight Definition: the weight of the light of one light at the collision that a multiple scattering volume
    // returned, against the equiangular point that the volume lights itself (Volume.EquiangularShare). It is that of the
    // collision in the balance heuristic, mu_t W / (mu_t W + p_ea), with p_ea the equiangular density of the collision about
    // a point of that light: a point drawn as the volume draws its own (from where the ray starts), which need not be the
    // same point, since the two weights add up to one for every point and are each used in the mean over the points.
    //
    // With "doublescattering" on a multiple scattering volume (Volume.JointShare) the collision has a third rival for a
    // light at a fixed position: the two points drawn jointly from the ray before (VolumeJoint), whose density over what
    // the two strategies above share, mu_t W of the collision before times the phase function there, is added below.
    public sealed class VolumeDirectWeight
    {
        public Ray ray;
        public Float distance, t;
        // mu_t W at the collision, its mean over the wavelengths
        public Float product;
        // Whether the volume draws an equiangular point on this ray ("distancesampling" other than "density")
        public bool equiangular = true;
        // "distancesampling" "equiangular": the lights that have a position are left to the equiangular point alone
        public bool equiangularOnly;
        // The collision before this one on the path, where the volume also draws the two jointly; null otherwise
        public VolumeJoint joint;
        // For a collision on a curved path (RefractiveVolume): `ray`, `distance` and `t` are those of the straight ray
        // that stands for the curve, and the point of a light that the equiangular density is about is where the curve puts
        // it (CurvedRayMedium.Pivot); `worldOrigin` is where the path really started
        public CurvedRayMedium curve;
        public Point3f worldOrigin;
        // (and the collision in the scene, with what the densities of the rivals that are put in the scene -- one more
        // scattering from the collision before -- are multiplied by there: JointOnCurve.Stretch)
        public Point3f worldPoint;
        public Float stretch = 1;

        // `hasPoint`: the light has a point to draw about (`pivot`); `fixedPoint`: it is the only one (a light at a position)
        public Float For(bool hasPoint, bool fixedPoint, Point3f pivot)
        {
            if (!hasPoint)
            {
                return 1;
            }
            Float pdf = 0, own = product;
            Point3f lightPoint = pivot, collision = ray.Apply(t);
            if (curve != null)
            {
                pivot = curve.Pivot(pivot);
                collision = worldPoint;
            }
            if (equiangular)
            {
                EquiangularDensity about = EquiangularDensity.Create(ray, 0, distance, pivot);
                if (about.valid)
                {
                    pdf = about.PDF(t);
                    if (equiangularOnly)
                    {
                        own = 0;
                    }
                }
            }
            Float jointly = joint != null && fixedPoint ? (joint.Ratio(lightPoint, collision) + joint.OnceMore(lightPoint, collision)) * stretch : 0;
            if (pdf == 0 && jointly == 0)
            {
                return 1;
            }
            return own + pdf + jointly > 0 ? own / (own + pdf + jointly) : 1;
        }
    }

    // VolumeJoint Definition: a collision c of a multiple scattering volume that draws two points of its rays jointly
    // ("doublescattering", Volume.JointShare), for the weights of the light at the collision b that follows it: the ray that
    // c was found on with the part of it that the medium fills, where c is, mu_t W there (its mean over the wavelengths),
    // and the value of the phase function for the direction that the path took from c.
    //
    // The same collision is where once-more scattered next-event estimation puts its one scattering (Volume.OnceMoreShare):
    // for its density at b, the phase function of c and which of the two implementations the medium asks for.
    public sealed class VolumeJoint
    {
        public Ray ray;
        public Float tMin, tMax, t;
        public Float product;
        public Float phasePdf;
        // Whether the volume draws two points jointly on its rays ("doublescattering")
        public bool jointly;
        // The phase function at c, and how the volume samples one more scattering from c ("oncemore")
        public PhaseFunction phase;
        public VolumeOnceMore onceMore;
        // For a point of a surface in the place of c (`ray` starts there, `t` and the product are 0 and 1, `phase` is that of
        // the medium there): the normal on the side that the path came from, the only side that is sampled
        public bool sided;
        public Normal3f side;
        // For a collision on a curved path (RefractiveVolume): `ray` and `t` are those of the straight ray that stands for
        // the curve; the collision is at `worldC` in the scene, where the path came along -`worldWo`
        public bool onCurve;
        public Point3f worldC;
        public Vector3f worldWo;

        // The density of the scattering b that is put between c and the point a of a light, over the phase function of the
        // direction that the path took from c: what the densities of the path and of the equiangular point have in common
        // after mu_t W of c
        public Float OnceMore(Point3f a, Point3f b)
        {
            if (onceMore == VolumeOnceMore.None || !(phasePdf > 0))
            {
                return 0;
            }
            Point3f c = onCurve ? worldC : ray.Apply(t);
            if (sided && (!(Dot(a - c, side) > 0) || !(Dot(b - c, side) > 0)))
            {
                return 0;
            }
            return OnceMoreScattering.Density(onceMore, phase, onCurve ? worldWo : -ray.d, c, b, a) / phasePdf;
        }

        // The density with which c and b are drawn jointly about a, over mu_t W of c
        public Float Jointly(Point3f a, Point3f b)
        {
            if (!jointly)
            {
                return 0;
            }
            InverseDistanceDensity along = InverseDistanceDensity.Create(ray, tMin, tMax, a);
            Float squared = LengthSquared(a - b);
            if (!along.valid || !(squared > 0) || !(product > 0))
            {
                return 0;
            }
            return along.PDF(t) * Distance(a, ray.Apply(t)) / (Pi * Pi * Pi * squared) / product;
        }

        // The density with which the two were drawn jointly at c and at b about the point a of a light,
        // p(c) |ac| / (pi^3 |ab|^2), over mu_t W of c times the phase function: what the densities of the path and of the
        // equiangular point have in common
        public Float Ratio(Point3f a, Point3f b)
        {
            return phasePdf > 0 ? Jointly(a, b) / phasePdf : 0;
        }
    }

    // VolumeGeometry Definition: what Integrate() and GetNearestHit() found. A surface, or a point of a volume with its phase function
    public sealed unsafe class VolumeGeometry
    {
        public bool isSurface;
        public ShapeIntersection surface;
        public MediumInteraction scatter;
        public PhaseFunction phase;
        // Where the volume gathers a share of the light that arrives at a point of the ray straight from a light by itself
        // (the multiple scattering volumes with "distancesampling"): what gives the weight of that light at this point, for
        // the next-event estimation there and for a light that the next ray reaches. Null otherwise: the weight is one.
        public VolumeDirectWeight direct;
        // Where the volume draws two points jointly ("doublescattering" on a multiple scattering volume): this collision,
        // for the weights at the next one (the path tracer adds the phase function of the direction it takes, and gives it
        // back in VolumeContext.joint for the ray that leaves from here). Null otherwise.
        public VolumeJoint joint;
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

        // The same without the transmittance of the beam: no shadow ray (the course's four-argument EvaluateLightSample, for
        // the density that SingleScatterPDFVolume builds)
        public abstract bool GenerateLightSample(VolumeContext ctx, MediumInteraction point, out Vector3f wi, out SampledSpectrum L);

        // The first with what the heuristic needs: the density of the direction (with the probability of the light), and whether
        // the light is a point or a direction that no other strategy finds
        public virtual bool GenerateLightSample(VolumeContext ctx, MediumInteraction point, out Vector3f wi, out SampledSpectrum L, out Float lightPdf,
                                                out bool delta, out SampledSpectrum beamTransmittance, out SampledSpectrum beamR)
        {
            lightPdf = 0;
            delta = true;
            return GenerateLightSample(ctx, point, null, out wi, out L, out beamTransmittance, out beamR);
        }

        // What arrives at the point from the direction given, which was sampled with the density `scatterPdf` (the course's
        // EvaluateLightSample): the radiance of the light that the direction reaches, with the weight of the heuristic against
        // the sampling of that light, and the transmittance of the beam; false if it reaches none. `emitted` is what the volumes
        // on the way emit towards the point, whether a light is reached or not (section 3.8: Volume.Emission), already
        // attenuated and divided by the densities of its own estimate.
        // The emission of a medium that is sampled as a light (media_emission_light.cs), from a point of a volume: a direction
        // towards the emission, and what the volumes emit along it, times the phase function over the density of the
        // direction, over the mean of the densities `r` of the path with those of the beam. `weighted`: with the weight against
        // the direction of the phase function, which EvaluateLightSample gives the other share. False where no medium is
        // sampled so.
        public virtual bool SampleEmission(VolumeContext ctx, MediumInteraction point, PhaseFunction phase, SampledSpectrum r, bool weighted,
                                           out SampledSpectrum emitted)
        {
            emitted = new SampledSpectrum(0f);
            return false;
        }

        public virtual bool EvaluateLightSample(VolumeContext ctx, MediumInteraction point, Vector3f wi, Float scatterPdf, out SampledSpectrum L,
                                                out SampledSpectrum beamTransmittance, out SampledSpectrum beamR, out SampledSpectrum emitted)
        {
            L = new SampledSpectrum(0f);
            beamTransmittance = new SampledSpectrum(1f);
            beamR = new SampledSpectrum(1f);
            emitted = new SampledSpectrum(0f);
            return false;
        }

        // A light for the ray that starts at the point of the context, with a point of it if it has a position: the point
        // about which the equiangular density is taken (section 4.2.3: a point chosen on an area light) and the joint
        // importance sampling of the double scattering is made; false if there is no light
        // In a scene where a volume bends the rays: a direction in which a ray from a point of the path reaches the point
        // `to`, one of those there may be, with the number of searches that stands for one over the probability of finding
        // it and the area across the ray at `to` per solid angle at the start
        public virtual bool ConnectBent(VolumeContext ctx, Interaction from, VolumePlace place, Point3f to, out Vector3f direction, out Float searches, out Float spread)
        {
            direction = default;
            searches = 0;
            spread = 0;
            return false;
        }

        public virtual bool ChooseLight(VolumeContext ctx, out VolumeLight light)
        {
            light = null;
            return false;
        }

        // The transmittance from a point of the path to any point, through the volumes and the surfaces that only bound them
        // (zero and false behind a surface that stops the ray), with where the point is
        public virtual bool Connect(VolumeContext ctx, MediumInteraction from, Point3f to, out SampledSpectrum beamTransmittance, out SampledSpectrum beamR,
                                    out VolumePlace place)
        {
            beamTransmittance = new SampledSpectrum(0f);
            beamR = new SampledSpectrum(1f);
            place = null;
            return false;
        }

        // A sample of the light chosen for a point at a place that Connect() gave (null: the point is where the path is): its
        // direction, its radiance over its density and over the probability of the light, and the transmittance of the beam
        // from it
        public virtual bool EvaluateLight(VolumeContext ctx, VolumeLight light, VolumePlace place, MediumInteraction point, out Vector3f wi, out SampledSpectrum L,
                                          out SampledSpectrum beamTransmittance, out SampledSpectrum beamR)
        {
            wi = default;
            L = new SampledSpectrum(0f);
            beamTransmittance = new SampledSpectrum(0f);
            beamR = new SampledSpectrum(1f);
            return false;
        }

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

        // The transmittance along a ray that is carried through the volume without scattering, for a volume that may bend
        // it. For a volume of straight paths (every one but RefractiveVolume) it is Transmittance() over the first tMax of
        // the ray, and `bent` is false. A volume that bends the ray follows it to the first surface, or out of the scene,
        // whatever tMax says: `hit`, P and g are that surface as Integrate would return it, and `arrival` the ray there.
        public virtual SampledSpectrum Transmit(VolumeServices rs, Ray entering, Float tMax, out bool bent, out bool hit, out Point3f P, out VolumeGeometry g,
                                                out Ray arrival)
        {
            bent = false;
            hit = false;
            P = entering.Apply(tMax);
            g = null;
            arrival = entering;
            if (m_ctx.geometryOnly)
            {
                return new SampledSpectrum(1f);
            }
            return Transmittance(rs, entering.o, P);
        }

        // The radiance that the volume emits between the two points towards the first (section 3.8), attenuated by the volume
        // itself: int T(P0, x) sigma_a Le dx. An estimate whose densities are divided out already.
        public abstract SampledSpectrum Emission(VolumeServices rs, Point3f P0, Point3f P1);

        // For a volume that may bend a ray (Medium.BendsRays): what Transmit gives, with the emission along the path of the
        // ray if it is wanted (as EmissionOver gives it, with the densities `density` of the path that reaches the ray). The
        // transmittance is zero where the ray, as it leaves, does not meet the box `further` (nothing is wanted beyond).
        public virtual SampledSpectrum Gather(VolumeServices rs, Ray entering, Float tMax, bool gather, SampledSpectrum density, Bounds3f further,
                                              out SampledSpectrum emitted, out bool bent, out bool hit, out Point3f P, out VolumeGeometry g, out Ray arrival)
        {
            bent = false;
            hit = false;
            P = default;
            g = null;
            arrival = entering;
            Point3f end = entering.Apply(tMax);
            emitted = gather ? EmissionOver(rs, entering.o, end, density) : new SampledSpectrum(0f);
            Float t0, t1;
            return further.IntersectP(end, entering.d, Infinity, &t0, &t1) ? Transmittance(rs, entering.o, end) : new SampledSpectrum(0f);
        }

        // The same over the mean, over the wavelengths, of the densities of the whole path: those that reach P0, `density`,
        // times those of the tracking between the points where one wavelength samples it for the others. (The mean of a
        // product, and not the product of the means of the path and of the ray: the two lean the same way for a wavelength.)
        public SampledSpectrum EmissionOver(VolumeServices rs, Point3f P0, Point3f P1, SampledSpectrum density)
        {
            m_ctx.emissionDensity = density;
            m_ctx.emissionDensityUsed = false;
            SampledSpectrum emitted = Emission(rs, P0, P1);
            if (!m_ctx.emissionDensityUsed)
            {
                emitted /= density.Average();
            }
            m_ctx.emissionDensity = new SampledSpectrum(1f);
            m_ctx.emissionDensityUsed = false;
            return emitted;
        }

        // The light scattered once at the point (the course's listings 10 and 11): `numerator` (the scattering coefficient,
        // the transmittance and the weight of the point over its density) times the phase function and the light, over the
        // mean of the densities `r` of the path with those of the beam. The lights are sampled; with "lightsampling" "mis" the
        // phase function is sampled too, each with the weight of the heuristic, and the direction of the phase function also
        // gathers what the volumes emit towards the point.
        protected SampledSpectrum DirectLighting(VolumeServices rs, MediumInteraction intr, PhaseFunction phase, SampledSpectrum numerator, SampledSpectrum r)
        {
            SampledSpectrum sum = new SampledSpectrum(0f);
            if (!(bool)numerator || !(bool)r)
            {
                return sum;
            }
            bool phaseSampling = medium.volumeIntegrator.phaseSampling;
            Point3f saved = m_ctx.P;
            m_ctx.SetP(intr.p());
            Vector3f direction;
            SampledSpectrum light, beam, beamR;
            Float lightPdf;
            bool delta;
            if (rs.GenerateLightSample(m_ctx, intr, out direction, out light, out lightPdf, out delta, out beam, out beamR))
            {
                Float w = phaseSampling && !delta ? PowerHeuristic(1, lightPdf, 1, phase.PDF(intr.wo, direction)) : 1;
                SampledSpectrum rBeam = r * beamR;
                if ((bool)rBeam)
                {
                    sum += w * numerator * phase.p(intr.wo, direction) * light * beam / rBeam.Average();
                }
            }
            SampledSpectrum sampledEmission;
            if (rs.SampleEmission(m_ctx, intr, phase, r, phaseSampling, out sampledEmission))
            {
                sum += numerator * sampledEmission;
            }
            if (phaseSampling)
            {
                pstd.optional<PhaseFunctionSample> ps = phase.Sample_p(intr.wo, new Point2f(rs.GenerateRandomNumber(), rs.GenerateRandomNumber()));
                if (ps && ps.value().pdf > 0)
                {
                    SampledSpectrum emitted;
                    Float ratio = ps.value().p / ps.value().pdf;
                    if (rs.EvaluateLightSample(m_ctx, intr, ps.value().wi, ps.value().pdf, out light, out beam, out beamR, out emitted))
                    {
                        SampledSpectrum rBeam = r * beamR;
                        if ((bool)rBeam)
                        {
                            sum += numerator * ratio * light * beam / rBeam.Average();
                        }
                    }
                    if ((bool)emitted)
                    {
                        sum += numerator * ratio * emitted / r.Average();
                    }
                }
            }
            m_ctx.SetP(saved);
            return sum;
        }

        // The part of the ray that the medium may fill: the extent that the medium says it has, or, for a ray without end in a
        // homogeneous medium, as far as anything gets through; false for a ray without end in a medium that has no extent
        protected bool Span(Ray ray, Float distance, out Float t0, out Float t1)
        {
            t0 = 0;
            t1 = distance;
            Bounds3f bounds;
            Transform renderFromMedium;
            MajorantGrid grid;
            if (medium.AggregateRegion(out bounds, out renderFromMedium, out grid))
            {
                Float tMax = distance;
                Ray inMedium = renderFromMedium.ApplyInverse(ray, &tMax);
                Float a = 0, b = 0;
                if (!bounds.IntersectP(inMedium.o, inMedium.d, tMax, &a, &b))
                {
                    t1 = 0;
                    return true;
                }
                // (the transformation keeps the parameter of the ray)
                t0 = a;
                t1 = b;
                return true;
            }
            if (IsInf(distance) && medium is HomogeneousMedium)
            {
                // A ray without end in a medium that is the same everywhere: marched as far as the transmittance of the
                // wavelength that is absorbed least is still above Negligible
                MediumProperties mp = medium.SamplePoint(ray.o, m_ctx.lambda, ray.time);
                Float least = (mp.sigma_a + mp.sigma_s).MinComponentValue();
                if (!(least > 0))
                {
                    return false;
                }
                t1 = -std.log(Negligible) / least;
                return true;
            }
            return !IsInf(distance);
        }


        // The light scattered twice along the ray (the single scattering volumes with "doublescattering"): at a point c of the
        // ray towards a point b anywhere, and at b towards the eye of the ray, lit there by one light.
        //
        // "joint": the joint importance sampling of Georgiev, Krivanek, Hachisuka, Nowrouzezahrai and Jarosz, "Joint Importance
        // Sampling of Low-Order Volumetric Scattering" (SIGGRAPH Asia 2013), as in Novak et al. 2018, section 6.4, equations
        // 56 to 61. With a point a of the light, c and b are drawn in proportion to the product of the two geometry terms,
        // 1 / (|ab|^2 |bc|^2), in three steps, each from the marginal of what is left:
        //     the distance of c along the ray        in proportion to 1 / |ac|                      (InverseDistanceDensity)
        //     the direction from c, by its angle theta to the direction of a      (pi - theta) / (pi^3 sin theta)
        //     the distance of b along it             the equiangular density about a over the whole direction
        // whose product is |ac| / (pi^3 |ab|^2) with the first. The phase functions and the transmittances are in the weight
        // (the paper's tables for phase functions that are not constant are not made).
        // "plain", and "joint" for a light that has no position: c uniform over the ray, the direction from the phase function,
        // the distance with the density of the extinction at c: what a path would do.
        // "mis": one of the two with the probability one half, over the mean of the densities of both (the joint densities
        // put b near the light, which is where the light is strongest if the medium reaches there and nowhere useful if the
        // light is outside the medium).
        //
        // The renderer connects c to b (the volumes and the surfaces between them, and what volume b is in: Connect) and
        // lights b (EvaluateLight); the transmittance to c is this volume's own.
        protected SampledSpectrum DoubleScattering(VolumeServices rs, Ray wi, Float distance)
        {
            SampledSpectrum none = new SampledSpectrum(0f);
            VolumeIntegrator settings = medium.volumeIntegrator;
            if (settings.doubleScattering == VolumeDoubleScattering.None || !m_ctx.mayScatter || !m_ctx.mayScatterTwice)
            {
                return none;
            }
            Float tMin, tMax;
            if (!Span(wi, distance, out tMin, out tMax) || !(tMax > tMin))
            {
                return none;
            }
            VolumeLight light;
            if (!rs.ChooseLight(m_ctx, out light))
            {
                return none;
            }
            SampledWavelengths lambda = m_ctx.lambda;
            InverseDistanceDensity along = default;
            if (settings.doubleScattering != VolumeDoubleScattering.Plain && light.hasPoint)
            {
                along = InverseDistanceDensity.Create(wi, tMin, tMax, light.point);
            }
            // The probability of the joint strategy
            Float pJoint = along.valid ? (settings.doubleScattering == VolumeDoubleScattering.MIS ? 0.5f : 1) : 0;
            bool joint = pJoint == 1 || (pJoint > 0 && rs.GenerateRandomNumber() < pJoint);
            // The first scattering, on the ray
            Float t = joint ? along.Sample(rs.GenerateRandomNumber()) : Lerp(rs.GenerateRandomNumber(), tMin, tMax);
            if (!(t >= tMin) || !(t <= tMax))
            {
                return none;
            }
            Point3f c = wi.Apply(t);
            MediumProperties mp = medium.SamplePoint(c, lambda, wi.time);
            Float mean = (mp.sigma_a + mp.sigma_s).Average();
            if (!(bool)mp.sigma_s || !(mean > 0))
            {
                return none;
            }
            SampledSpectrum saved = m_ctx.r;
            m_ctx.r = new SampledSpectrum(1f);
            SampledSpectrum toC = Transmittance(rs, wi.o, c);
            SampledSpectrum r = saved * m_ctx.r;
            m_ctx.r = saved;
            if (!(bool)toC || !(bool)r)
            {
                return none;
            }
            // The second, anywhere
            Vector3f w;
            Float s;
            if (joint)
            {
                Vector3f toLight = light.point - c;
                Float far = Length(toLight);
                if (!(far > 0))
                {
                    return none;
                }
                Vector3f axis = toLight / far, v1, v2;
                CoordinateSystem(axis, &v1, &v2);
                Float theta = Pi * (1 - SafeSqrt(1 - rs.GenerateRandomNumber())), phi = 2 * Pi * rs.GenerateRandomNumber();
                Float sin = std.sin(theta), cos = std.cos(theta);
                w = sin * std.cos(phi) * v1 + sin * std.sin(phi) * v2 + cos * axis;
                Float h = far * sin, foot = far * cos;
                s = foot + h * std.tan(rs.GenerateRandomNumber() * (Pi - theta) + theta - Pi / 2);
            }
            else
            {
                pstd.optional<PhaseFunctionSample> ps = mp.phase.Sample_p(-wi.d, new Point2f(rs.GenerateRandomNumber(), rs.GenerateRandomNumber()));
                if (!ps || !(ps.value().pdf > 0))
                {
                    return none;
                }
                w = ps.value().wi;
                s = -std.log(1 - rs.GenerateRandomNumber()) / mean;
            }
            if (!(s > 0) || IsInf(s))
            {
                return none;
            }
            // The density of the two points, of the strategy taken or, with "mis", of the two with their probabilities (the
            // joint one: the density of c times those of the direction and of the distance multiplied, |ac| / (pi^3 |ab|^2))
            Float pdf = 0;
            if (pJoint > 0)
            {
                Float squared = LengthSquared(light.point - (c + s * w));
                if (squared > 0)
                {
                    pdf += pJoint * along.PDF(t) * Distance(light.point, c) / (Pi * Pi * Pi * squared);
                }
            }
            if (pJoint < 1)
            {
                pdf += (1 - pJoint) * mp.phase.PDF(-wi.d, w) * mean * std.exp(-mean * s) / (tMax - tMin);
            }
            if (!(pdf > 0))
            {
                return none;
            }
            Float pdfT = 1;
            Point3f b = c + s * w;
            Point3f savedP = m_ctx.P;
            m_ctx.SetP(c);
            SampledSpectrum result = none;
            MediumInteraction atC = new MediumInteraction(c, -wi.d, wi.time, medium, mp.phase);
            SampledSpectrum between, betweenR;
            VolumePlace place;
            if (rs.Connect(m_ctx, atC, b, out between, out betweenR, out place) && place.medium != null)
            {
                MediumProperties at = place.medium.SamplePoint(b, lambda, wi.time);
                if ((bool)at.sigma_s)
                {
                    MediumInteraction atB = new MediumInteraction(b, -w, wi.time, place.medium, at.phase);
                    Vector3f toLight;
                    SampledSpectrum arriving, beam, beamR;
                    if (rs.EvaluateLight(m_ctx, light, place, atB, out toLight, out arriving, out beam, out beamR))
                    {
                        r *= betweenR * beamR;
                        if ((bool)r)
                        {
                            result = toC * mp.sigma_s * mp.phase.p(-wi.d, w) * between * at.sigma_s * at.phase.p(-w, toLight) * arriving * beam /
                                     (pdfT * pdf * r.Average());
                        }
                    }
                }
            }
            m_ctx.SetP(savedP);
            return result;
        }

        private const Float Negligible = 1e-4f;

        // Equiangular sampling beside next-event estimation in a volume that scatters any number of times (Miller, Georgiev
        // and Jarosz, "A null-scattering path integral formulation of light transport", SIGGRAPH 2019, section 5.2 and figure
        // 7; "distancesampling" "mis" or "equiangular" on a multiple scattering volume). The light that is scattered once more
        // on this ray and then reaches a light is sampled twice: at the collision that the walk finds, where the path tracer
        // lights it (next-event estimation), and at a point drawn from the equiangular density about a point of a light,
        // which the volume lights itself, from that light. The two are weighed by the balance heuristic over their densities
        // for the whole path, null collisions included: with W the product of mu_n / mu_bar over the null collisions before
        // the point,
        //     the collision of the walk     mu_t W / (mu_t W + p_ea),
        //     the equiangular point         p_ea / (mu_t W + p_ea),
        // which add up to one for any one path. The weights are those of one light: the equiangular point takes the light of
        // the light that it was drawn about, over the probability of that light, and the path tracer weighs each light at the
        // collision by its own equiangular density (VolumeDirectWeight). mu_t W is taken as its mean over the wavelengths, so
        // that the weights do not depend on which wavelength the walk was made with. In a homogeneous volume W is the
        // transmittance itself.
        //
        // This is the equiangular point's share: `through` its transmittance (an estimate, with the densities `r` of the
        // estimate times those of the path), `product` the mu_t W of the point by wavelength, `at` the medium there.
        protected SampledSpectrum EquiangularShare(VolumeServices rs, Ray wi, Float t, Float pdf, MediumProperties at, SampledSpectrum through, SampledSpectrum product,
                                                   SampledSpectrum r, VolumeLight chosen)
        {
            SampledSpectrum none = new SampledSpectrum(0f);
            if (!(pdf > 0) || !(bool)at.sigma_s || !(bool)through || !(bool)r)
            {
                return none;
            }
            Point3f point = wi.Apply(t);
            // (the third rival, for a light at a position: the two points drawn jointly from the ray before)
            Float jointly = !FixedPoint(chosen) ? 0 : m_ctx.jointOnCurve != null ? m_ctx.jointOnCurve.Rivals(chosen.worldPoint, t)
                            : m_ctx.joint != null ? m_ctx.joint.Ratio(chosen.point, point) + m_ctx.joint.OnceMore(chosen.point, point) : 0;
            Float own = medium.volumeIntegrator.distanceSampling == VolumeDistanceSampling.Equiangular ? 0 : product.Average();
            Float share = pdf / (own + pdf + jointly);
            MediumInteraction intr = new MediumInteraction(point, -wi.d, wi.time, medium, at.phase);
            Point3f saved = m_ctx.P;
            m_ctx.SetP(point);
            Vector3f toLight;
            SampledSpectrum arriving, beam, beamR;
            SampledSpectrum result = none;
            if (rs.EvaluateLight(m_ctx, chosen, null, intr, out toLight, out arriving, out beam, out beamR))
            {
                SampledSpectrum rBeam = r * beamR;
                if ((bool)rBeam)
                {
                    result = through * at.sigma_s * at.phase.p(-wi.d, toLight) * arriving * beam * (share / (pdf * rBeam.Average()));
                }
            }
            m_ctx.SetP(saved);
            return result;
        }

        // What weighs the lights at the collision of the walk against the equiangular point (VolumeDirectWeight)
        protected VolumeDirectWeight CollisionWeight(Ray wi, Float distance, Float t, SampledSpectrum product)
        {
            VolumeDirectWeight weight = new VolumeDirectWeight();
            weight.ray = wi;
            weight.distance = distance;
            weight.t = t;
            weight.product = product.Average();
            weight.equiangular = medium.volumeIntegrator.distanceSampling != VolumeDistanceSampling.Density;
            weight.equiangularOnly = medium.volumeIntegrator.distanceSampling == VolumeDistanceSampling.Equiangular;
            weight.joint = m_ctx.jointOnCurve != null ? m_ctx.jointOnCurve.joint : m_ctx.joint;
            return weight;
        }

        // A light at a position: the only point that the samplings about a point of a light can be about
        protected static bool FixedPoint(VolumeLight light)
        {
            return light.hasPoint && light.fixedPoint;
        }

        // Whether this volume draws two points jointly on its rays (JointShare)
        protected bool Joint()
        {
            VolumeDoubleScattering mode = medium.volumeIntegrator.doubleScattering;
            return !m_ctx.polarised && (mode == VolumeDoubleScattering.Joint || mode == VolumeDoubleScattering.MIS);
        }

        // The collision of a walk as VolumeGeometry.joint
        protected VolumeJoint JointAt(Ray wi, Float distance, Float t, SampledSpectrum product, PhaseFunction phase)
        {
            VolumeOnceMore onceMore = medium.volumeIntegrator.onceMore;
            if (!Joint() && onceMore == VolumeOnceMore.None)
            {
                return null;
            }
            VolumeJoint joint = new VolumeJoint();
            joint.ray = wi;
            joint.t = t;
            joint.product = product.Average();
            joint.phase = phase;
            joint.onceMore = onceMore;
            Float tMin, tMax;
            if (Joint() && Span(wi, distance, out tMin, out tMax) && tMax > tMin)
            {
                joint.jointly = true;
                joint.tMin = tMin;
                joint.tMax = tMax;
            }
            return joint;
        }

        // The transmittance from the start of a ray to a point of it by ratio tracking with the tracker of the medium, with
        // the densities of the walk for the other wavelengths and the product of mu_n / mu_bar over its null collisions
        // (which is that transmittance as a path finds it: W); false where nothing gets through
        public bool ReachTo(VolumeServices rs, Ray ray, Float t, out SampledSpectrum through, out SampledSpectrum densities, out SampledSpectrum nulls)
        {
            return Reach(rs, ray, t, out through, out densities, out nulls);
        }

        // For a volume that may bend a ray (Medium.BendsRays): Reach along the path of a ray up to the point b that the
        // path passes through, with what the rivals of one more scattering need there (RefractiveVolume)
        public virtual bool ReachBent(VolumeServices rs, Ray from, Point3f b, Point3f lightPoint, out SampledSpectrum through, out SampledSpectrum densities,
                                      out SampledSpectrum nulls, out Float equiangular, out Vector3f arrival, out Float index, out Float seen)
        {
            through = densities = nulls = new SampledSpectrum(0f);
            equiangular = 0;
            arrival = from.d;
            index = 1;
            seen = 1;
            return false;
        }

        protected virtual bool Reach(VolumeServices rs, Ray ray, Float t, out SampledSpectrum through, out SampledSpectrum densities, out SampledSpectrum nulls)
        {
            SampledSpectrum T_ray = new SampledSpectrum(1f), rl = new SampledSpectrum(1f), ru = new SampledSpectrum(1f), product = new SampledSpectrum(1f);
            RNG walk = rs.rng;
            SampledSpectrum T_last = medium.volumeIntegrator.tracker.ForTransmittance().SampleT_maj(ray, medium, t, rs.GenerateRandomNumber(), ref walk, m_ctx.lambda,
                (Point3f p, MediumProperties mp, SampledSpectrum sigma_maj, SampledSpectrum T_maj) =>
                {
                    SampledSpectrum sigma_n = ClampZero(sigma_maj - mp.sigma_a - mp.sigma_s);
                    MediumEvents.RatioTrackingStep(ref T_ray, ref rl, ref ru, T_maj, sigma_maj, sigma_n);
                    for (int i = 0; i < NSpectrumSamples; ++i)
                    {
                        if (sigma_maj[i] > 0)
                        {
                            product[i] *= sigma_n[i] / sigma_maj[i];
                        }
                    }
                    return (bool)T_ray;
                });
            rs.rng = walk;
            if ((bool)T_ray)
            {
                MediumEvents.FinishSegment(ref T_ray, ref rl, ref ru, T_last);
            }
            through = T_ray;
            densities = rl;
            nulls = product;
            return (bool)T_ray && (bool)rl;
        }

        // The point that once-more scattering starts from: a collision of the volume, with its phase function, or a point of
        // a surface that the volume touches, with its BSDF (then only towards the side that the path came from: `side`)
        public sealed class OnceMoreStart
        {
            public Interaction point;
            public PhaseFunction phase;
            public BSDF bsdf;
            public bool surface;
            public Normal3f side;
            // (the volume of the points on the path's side of a surface, with the stack of the volumes there)
            public VolumePlace place;

            public Point3f P() { return point.p(); }

            public SampledSpectrum f(Vector3f w)
            {
                return surface ? bsdf.f(point.wo, w) * AbsDot(w, ((SurfaceInteraction)point).shading.n) : new SampledSpectrum(phase.p(point.wo, w));
            }

            public Float PDF(Vector3f w)
            {
                return surface ? bsdf.PDF(point.wo, w) : phase.PDF(point.wo, w);
            }

            public bool Faces(Vector3f w)
            {
                return !surface || Dot(w, side) > 0;
            }
        }

        // What the two implementations of once-more scattering do alike once b is chosen: b has to be a point of this volume
        // that the start sees, the stretch to it is walked (Reach), the light is asked for (EvaluateLight), and the share
        // against the rivals for this b is taken by the balance heuristic over the densities of the whole path, per mu_t W of
        // the start (one, for a surface):
        //     the path           p(c -> b)  mu_t(b) W(b)
        //     the equiangular b  p(c -> b)  p_ea(b)                  (with "distancesampling" "mis" / "equiangular")
        //     jointly            p(c, b) / (mu_t(c) W(c))            (with "doublescattering", from the ray that c is on)
        //     once more          `density`
        // `integrand`: what multiplies the transmittances, the scattering coefficient at b and the light in the estimate.
        private SampledSpectrum OnceMoreFinish(VolumeServices rs, OnceMoreStart start, VolumeJoint at, VolumeLight light, Point3f b, Float density, Float integrand,
                                               bool pathWeights)
        {
            SampledSpectrum none = new SampledSpectrum(0f);
            Point3f origin = start.P();
            if (medium.BendsRays())
            {
                return OnceMoreFinishBent(rs, start, at, light, b, density, integrand, pathWeights);
            }
            Vector3f w = Normalize(b - origin);
            if (!start.Faces(w))
            {
                return none;
            }
            SampledSpectrum f = start.f(w);
            if (!(bool)f)
            {
                return none;
            }
            Float time = start.point.time;
            Ray ray = start.surface ? ((SurfaceInteraction)start.point).SpawnRay(w) : new Ray(origin, w, time);
            ray.d = w;
            Float first = Distance(ray.o, b);
            Point3f surfacePoint;
            Float surfaceDistance;
            VolumeGeometry surface;
            bool hit = rs.GetNearestHit(ray, out surfacePoint, out surfaceDistance, out surface);
            if (hit && !(first < surfaceDistance))
            {
                return none;
            }
            SampledSpectrum through, densities, nulls;
            if (!Reach(rs, ray, first, out through, out densities, out nulls))
            {
                return none;
            }
            MediumProperties mp = medium.SamplePoint(b, m_ctx.lambda, time);
            if (!(bool)mp.sigma_s)
            {
                return none;
            }
            Float own = ((mp.sigma_a + mp.sigma_s) * (pathWeights ? nulls : new SampledSpectrum(1f))).Average(), equiangular = 0;
            if (medium.volumeIntegrator.distanceSampling != VolumeDistanceSampling.Density)
            {
                EquiangularDensity about = EquiangularDensity.Create(ray, 0, hit ? surfaceDistance : Infinity, light.point);
                if (about.valid)
                {
                    equiangular = about.PDF(first);
                    if (medium.volumeIntegrator.distanceSampling == VolumeDistanceSampling.Equiangular)
                    {
                        own = 0;
                    }
                }
            }
            Float share = density / (density + start.PDF(w) * (own + equiangular) + (at != null ? at.Jointly(light.point, b) : 0));
            Point3f saved = m_ctx.P;
            m_ctx.SetP(b);
            SampledSpectrum result = none;
            MediumInteraction atB = new MediumInteraction(b, -w, time, medium, mp.phase);
            Vector3f wl;
            SampledSpectrum arriving, beam, beamR;
            if (rs.EvaluateLight(m_ctx, light, start.place, atB, out wl, out arriving, out beam, out beamR))
            {
                SampledSpectrum r = m_ctx.r * densities * beamR;
                if ((bool)r)
                {
                    result = through * mp.sigma_s * f * mp.phase.p(-w, wl) * arriving * beam * (integrand * share / r.Average());
                }
            }
            m_ctx.SetP(saved);
            return result;
        }

        // The same in a volume that bends the rays: b is where it was put in the scene; the direction that reaches it from
        // the start is searched (VolumeServices.ConnectBent, which gives one of the paths to b with the number of searches
        // that stands for one over the probability of finding it, and the area across the path at b per solid angle at the
        // start), and the stretch is walked along that path (ReachBent). In the estimate the squared distance of the
        // straight stretch gives way to that area, and the densities of the rivals that go by the direction at the start --
        // the path and the equiangular point, each per sigma -- are those of b in the scene by the same exchange:
        //     the path           p(c -> b)  mu_t(b) n W(b)  |cb|^2 / (area n)
        //     the equiangular b  p(c -> b)  p_ea(b)         |cb|^2 / (area n)
        private SampledSpectrum OnceMoreFinishBent(VolumeServices rs, OnceMoreStart start, VolumeJoint at, VolumeLight light, Point3f b, Float density, Float integrand,
                                                   bool pathWeights)
        {
            SampledSpectrum none = new SampledSpectrum(0f);
            Point3f origin = start.P();
            Float squared = DistanceSquared(origin, b);
            if (!(squared > 0))
            {
                return none;
            }
            Vector3f w;
            Float searches, spread;
            if (!rs.ConnectBent(m_ctx, start.point, start.place, b, out w, out searches, out spread) || !(spread > 0) || !start.Faces(w))
            {
                return none;
            }
            SampledSpectrum f = start.f(w);
            if (!(bool)f)
            {
                return none;
            }
            Float time = start.point.time;
            Ray ray = start.surface ? ((SurfaceInteraction)start.point).SpawnRay(w) : new Ray(origin, w, time);
            ray.d = w;
            SampledSpectrum through, densities, nulls;
            Float equiangular, index, seen;
            Vector3f arrival;
            if (!ReachBent(rs, ray, b, light.point, out through, out densities, out nulls, out equiangular, out arrival, out index, out seen))
            {
                return none;
            }
            MediumProperties mp = medium.SamplePoint(b, m_ctx.lambda, time);
            if (!(bool)mp.sigma_s)
            {
                return none;
            }
            Float own = ((mp.sigma_a + mp.sigma_s) * (pathWeights ? nulls : new SampledSpectrum(1f))).Average() * index;
            if (medium.volumeIntegrator.distanceSampling == VolumeDistanceSampling.Equiangular && equiangular > 0)
            {
                own = 0;
            }
            Float exchange = squared / (spread * index);
            Float share = density / (density + start.PDF(w) * (own + equiangular) * exchange + (at != null ? at.Jointly(light.point, b) : 0));
            Point3f saved = m_ctx.P;
            m_ctx.SetP(b);
            SampledSpectrum result = none;
            MediumInteraction atB = new MediumInteraction(b, -arrival, time, medium, mp.phase);
            Vector3f wl;
            SampledSpectrum arriving, beam, beamR;
            if (rs.EvaluateLight(m_ctx, light, start.place, atB, out wl, out arriving, out beam, out beamR))
            {
                SampledSpectrum r = m_ctx.r * densities * beamR;
                if ((bool)r)
                {
                    result = through * mp.sigma_s * f * mp.phase.p(-arrival, wl) * arriving * beam * (integrand * share * searches * seen * squared / (spread * r.Average()));
                }
            }
            m_ctx.SetP(saved);
            return result;
        }

        // The point b of the arc for the angles (theta, alpha, phi) between c and a; false where it is not a point
        private static bool OnceMorePoint(Point3f c, Point3f a, Float theta, Float alpha, Float phi, out Point3f b, out Float second)
        {
            b = c;
            second = 0;
            Vector3f axis = a - c, v1, v2;
            Float s = Length(axis);
            Float sin = std.sin(theta);
            if (!(s > 0) || !(sin > 0) || !(alpha > 0) || !(alpha < theta))
            {
                return false;
            }
            axis /= s;
            CoordinateSystem(axis, &v1, &v2);
            Float first = s * std.sin(theta - alpha) / sin;
            second = s * std.sin(alpha) / sin;
            if (!(first > 0) || !(second > 0) || IsInf(first) || IsInf(second))
            {
                return false;
            }
            b = c + first * (std.cos(alpha) * axis + std.sin(alpha) * (std.cos(phi) * v1 + std.sin(phi) * v2));
            return true;
        }

        // The light at a position that the start samples about; false for any other light, and for a surface when the light
        // is not on the side that the path came from
        private bool OnceMoreLight(VolumeServices rs, OnceMoreStart start, out VolumeLight light)
        {
            Point3f saved = m_ctx.P;
            m_ctx.SetP(start.P());
            bool chosen = rs.ChooseLight(m_ctx, out light) && FixedPoint(light);
            m_ctx.SetP(saved);
            return chosen && start.Faces(light.point - start.P());
        }

        // Once-more scattered next-event estimation (Hanika, Weidlich and Droske, "Once-more scattered next event estimation
        // for volume rendering", EGSR 2022; "oncemore" "omnee" on a multiple scattering volume), from a collision of the
        // volume or a point of a surface in it, for a light at a position a: one scattering b in between, with the angle of
        // its deflection drawn from the phase function and its place on the arc of that angle drawn so that the two inverse
        // squared distances cancel (OnceMoreScattering). As published: the angle is that of a direction drawn from the phase
        // function, and a direction that turns back is discarded (the forward half only); the transmittances, and the phase
        // function or BSDF at the start, are left in the weight; the medium may vary, and is then tracked. The paper connects
        // to the camera from the light; here the path comes from the camera and connects to the light, which is the same
        // construction. What is not sampled here (the angles beyond pi / 2) is the path's and the equiangular point's, by
        // the weights (OnceMoreFinish). `proposal`: the phase function that the angle is drawn from (that of the medium at
        // the start).
        public SampledSpectrum OnceMoreShare(VolumeServices rs, OnceMoreStart start, PhaseFunction proposal, VolumeJoint at, bool pathWeights)
        {
            SampledSpectrum none = new SampledSpectrum(0f);
            VolumeLight light;
            if (!OnceMoreLight(rs, start, out light))
            {
                return none;
            }
            Vector3f wo = start.point.wo;
            pstd.optional<PhaseFunctionSample> ps = proposal.Sample_p(wo, new Point2f(rs.GenerateRandomNumber(), rs.GenerateRandomNumber()));
            Float alphaSample = rs.GenerateRandomNumber(), phi = 2 * Pi * rs.GenerateRandomNumber();
            if (!ps || !(ps.value().pdf > 0))
            {
                return none;
            }
            Float cos = Dot(-wo, ps.value().wi);
            if (!(cos > 0))
            {
                return none;
            }
            Float theta = SafeACos(cos);
            Point3f b;
            Float second;
            if (!OnceMorePoint(start.P(), light.point, theta, alphaSample * theta, phi, out b, out second))
            {
                return none;
            }
            Float angle = OnceMoreScattering.Angle(VolumeOnceMore.OnceMore, proposal, wo, theta);
            if (!(angle > 0))
            {
                return none;
            }
            // (in the measure of the three angles the integrand has |ba|^2 / s of the volume, and the density is p / (2 pi theta))
            Float s = Distance(start.P(), light.point);
            Float inAngles = angle / (2 * Pi * theta);
            return OnceMoreFinish(rs, start, at, light, b, inAngles * s / (second * second), second * second / s / inAngles, pathWeights);
        }

        // The second implementation ("oncemore" "omnee2"), for what the paper says the first leaves out:
        //   - the angles beyond pi / 2: the angle is drawn over all of them, a quarter of the time uniformly, so that the
        //     weight theta / sin(theta) of an angle drawn from the phase function stays bounded where a path turns back;
        //   - the phase function or BSDF at the start, the scattering coefficient where the medium varies, and the
        //     transmittances: a few candidates are drawn and one is kept in proportion to what the first leaves in the
        //     weight -- the phase function or BSDF at the start towards b, the scattering coefficient and the phase function
        //     at b, and the transmittance of the two stretches as the extinctions at the start and at b would give it --
        //     over its density (resampled importance sampling: the estimate of the one kept is its integrand over that
        //     target, times the mean of the candidates' ratios of target to density).
        // The share against the rivals is that of the density of a candidate, which is what the rivals can know: the weights
        // add up to one for every path whatever the resampling makes of the candidates.
        public SampledSpectrum OnceMoreShare2(VolumeServices rs, OnceMoreStart start, PhaseFunction proposal, VolumeJoint at, bool pathWeights)
        {
            const int Candidates = 4;
            SampledSpectrum none = new SampledSpectrum(0f);
            VolumeLight light;
            if (!OnceMoreLight(rs, start, out light))
            {
                return none;
            }
            Point3f origin = start.P();
            Vector3f wo = start.point.wo;
            Float s = Distance(origin, light.point);
            MediumProperties here = medium.SamplePoint(origin, m_ctx.lambda, start.point.time);
            Float extinctionHere = (here.sigma_a + here.sigma_s).Average();
            Point3f* points = stackalloc Point3f[Candidates];
            Float* targets = stackalloc Float[Candidates];
            Float* densities = stackalloc Float[Candidates];
            Float* seconds = stackalloc Float[Candidates];
            Float sum = 0;
            for (int k = 0; k < Candidates; ++k)
            {
                targets[k] = 0;
                Float theta;
                Float pick = rs.GenerateRandomNumber(), u0 = rs.GenerateRandomNumber(), u1 = rs.GenerateRandomNumber();
                Float alphaSample = rs.GenerateRandomNumber(), phi = 2 * Pi * rs.GenerateRandomNumber();
                if (pick < OnceMoreScattering.Uniform)
                {
                    theta = Pi * u0;
                }
                else
                {
                    pstd.optional<PhaseFunctionSample> ps = proposal.Sample_p(wo, new Point2f(u0, u1));
                    if (!ps || !(ps.value().pdf > 0))
                    {
                        continue;
                    }
                    theta = SafeACos(Dot(-wo, ps.value().wi));
                }
                Float angle = OnceMoreScattering.Angle(VolumeOnceMore.OnceMore2, proposal, wo, theta);
                Point3f b;
                Float second;
                if (!(angle > 0) || !OnceMorePoint(origin, light.point, theta, alphaSample * theta, phi, out b, out second))
                {
                    continue;
                }
                Float first = Distance(origin, b);
                Vector3f w = (b - origin) / first;
                if (!start.Faces(w))
                {
                    continue;
                }
                MediumProperties mp = medium.SamplePoint(b, m_ctx.lambda, start.point.time);
                Float scattering = mp.sigma_s.Average();
                if (!(scattering > 0))
                {
                    continue;
                }
                Float extinction = 0.5f * (extinctionHere + (mp.sigma_a + mp.sigma_s).Average());
                Float target = start.f(w).Average() * scattering * mp.phase.p(-w, (light.point - b) / second) * std.exp(-extinction * (first + second));
                Float inAngles = angle / (2 * Pi * theta);
                if (!(target > 0) || !(inAngles > 0))
                {
                    continue;
                }
                points[k] = b;
                seconds[k] = second;
                densities[k] = inAngles;
                targets[k] = target;
                sum += target / inAngles;
            }
            if (!(sum > 0))
            {
                return none;
            }
            Float choose = rs.GenerateRandomNumber() * sum, running = 0;
            int kept = -1;
            for (int k = 0; k < Candidates; ++k)
            {
                if (!(targets[k] > 0))
                {
                    continue;
                }
                kept = k;
                running += targets[k] / densities[k];
                if (choose < running)
                {
                    break;
                }
            }
            Float second2 = seconds[kept] * seconds[kept];
            // (the integrand of the one kept over its target, times the mean of the ratios)
            Float integrand = second2 / s / targets[kept] * (sum / Candidates);
            return OnceMoreFinish(rs, start, at, light, points[kept], densities[kept] * s / second2, integrand, pathWeights);
        }

        // Joint importance sampling of two scatterings beside the path, in a volume that scatters any number of times
        // ("doublescattering" "joint" or "mis" on a multiple scattering volume; Georgiev et al. 2013 for the densities,
        // DoubleScattering above; Miller et al. 2019 for the weights, EquiangularShare above). The light that is scattered
        // twice more from this ray and then reaches a light at a position a is sampled by the path: a collision c of the walk
        // on this ray, a direction from the phase function there, a collision b of the walk on that ray, lit by the path
        // tracer; and here: c on the ray in proportion to 1 / |ac|, then the direction from c and the distance of b along it
        // jointly, in proportion to the two geometry terms 1 / (|cb|^2 |ba|^2), with b before any surface. With W the
        // product of mu_n / mu_bar over the null collisions of each of the two stretches, the densities of one such path are
        //     the path           mu_t(c) W(c)  p(c -> b)  mu_t(b) W(b)
        //     the equiangular b  mu_t(c) W(c)  p(c -> b)  p_ea(b)           (with "distancesampling" "mis" / "equiangular")
        //     jointly            p(c) |ac| / (pi^3 |ab|^2)
        // and each takes its share by the balance heuristic (VolumeDirectWeight for the first, EquiangularShare for the
        // second, here for the third), with mu_t W as its mean over the wavelengths. Only for a light at a position: the
        // weights are about the one point that such a light has. The path tracer says which collision a ray leaves from
        // (VolumeContext.joint).
        protected SampledSpectrum JointShare(VolumeServices rs, Ray wi, Float distance, bool pathWeights)
        {
            SampledSpectrum none = new SampledSpectrum(0f);
            if (!Joint() || !m_ctx.mayScatter || !m_ctx.mayScatterTwice)
            {
                return none;
            }
            Float tMin, tMax;
            if (!Span(wi, distance, out tMin, out tMax) || !(tMax > tMin))
            {
                return none;
            }
            VolumeLight light;
            if (!rs.ChooseLight(m_ctx, out light) || !FixedPoint(light))
            {
                return none;
            }
            InverseDistanceDensity along = InverseDistanceDensity.Create(wi, tMin, tMax, light.point);
            if (!along.valid)
            {
                return none;
            }
            // The first scattering, on the ray
            Float t = along.Sample(rs.GenerateRandomNumber());
            if (!(t >= tMin) || !(t <= tMax) || !(t > 0))
            {
                return none;
            }
            SampledWavelengths lambda = m_ctx.lambda;
            Point3f c = wi.Apply(t);
            SampledSpectrum toC, densitiesC, nullsC;
            if (!Reach(rs, wi, t, out toC, out densitiesC, out nullsC))
            {
                return none;
            }
            MediumProperties mp = medium.SamplePoint(c, lambda, wi.time);
            if (!(bool)mp.sigma_s)
            {
                return none;
            }
            // The second: the direction by its angle to that of the light, the distance equiangular about the light
            Vector3f toLight = light.point - c;
            Float far = Length(toLight);
            if (!(far > 0))
            {
                return none;
            }
            Vector3f axis = toLight / far, v1, v2;
            CoordinateSystem(axis, &v1, &v2);
            Float theta = Pi * (1 - SafeSqrt(1 - rs.GenerateRandomNumber())), phi = 2 * Pi * rs.GenerateRandomNumber();
            Float sin = std.sin(theta), cos = std.cos(theta);
            Vector3f w = sin * std.cos(phi) * v1 + sin * std.sin(phi) * v2 + cos * axis;
            Float s = far * cos + far * sin * std.tan(rs.GenerateRandomNumber() * (Pi - theta) + theta - Pi / 2);
            if (!(s > 0) || IsInf(s))
            {
                return none;
            }
            Point3f b = c + s * w;
            Float squared = LengthSquared(light.point - b);
            Float pdf = squared > 0 ? along.PDF(t) * far / (Pi * Pi * Pi * squared) : 0;
            if (!(pdf > 0))
            {
                return none;
            }
            // b is a point of this volume that the ray from c reaches before any surface
            Ray second = new Ray(c, w, wi.time);
            Point3f surfacePoint;
            Float surfaceDistance;
            VolumeGeometry surface;
            bool hit = rs.GetNearestHit(second, out surfacePoint, out surfaceDistance, out surface);
            if (hit && !(s < surfaceDistance))
            {
                return none;
            }
            SampledSpectrum toB, densitiesB, nullsB;
            if (!Reach(rs, second, s, out toB, out densitiesB, out nullsB))
            {
                return none;
            }
            MediumProperties at = medium.SamplePoint(b, lambda, wi.time);
            if (!(bool)at.sigma_s)
            {
                return none;
            }
            // The densities of the rivals for these two points
            Float phase = mp.phase.PDF(-wi.d, w);
            Float productC = ((mp.sigma_a + mp.sigma_s) * (pathWeights ? nullsC : new SampledSpectrum(1f))).Average();
            Float productB = ((at.sigma_a + at.sigma_s) * (pathWeights ? nullsB : new SampledSpectrum(1f))).Average();
            Float equiangular = 0;
            if (medium.volumeIntegrator.distanceSampling != VolumeDistanceSampling.Density)
            {
                EquiangularDensity about = EquiangularDensity.Create(second, 0, hit ? surfaceDistance : Infinity, light.point);
                if (about.valid)
                {
                    equiangular = about.PDF(s);
                    if (medium.volumeIntegrator.distanceSampling == VolumeDistanceSampling.Equiangular)
                    {
                        productB = 0;
                    }
                }
            }
            // (and the scattering that is put between c and the light, where the volume also does that)
            Float onceMore = OnceMoreScattering.Density(medium.volumeIntegrator.onceMore, mp.phase, -wi.d, c, b, light.point);
            Float share = pdf / (pdf + productC * (phase * (productB + equiangular) + onceMore));
            Point3f saved = m_ctx.P;
            m_ctx.SetP(b);
            SampledSpectrum result = none;
            MediumInteraction atB = new MediumInteraction(b, -w, wi.time, medium, at.phase);
            Vector3f wl;
            SampledSpectrum arriving, beam, beamR;
            if (rs.EvaluateLight(m_ctx, light, null, atB, out wl, out arriving, out beam, out beamR))
            {
                SampledSpectrum r = m_ctx.r * densitiesC * densitiesB * beamR;
                if ((bool)r)
                {
                    result = toC * mp.sigma_s * mp.phase.p(-wi.d, w) * toB * at.sigma_s * at.phase.p(-w, wl) * arriving * beam * (share / (pdf * r.Average()));
                }
            }
            m_ctx.SetP(saved);
            return result;
        }

        // How a spectral tracking walk ends: beyond the end of the ray, at a scattering, or with nothing left to follow
        protected enum WalkEnd
        {
            Left,
            Scattered,
            Ended
        }

        // What the probability of an event of spectral tracking is in proportion to (Kutz et al. 2017): the mean over the
        // wavelengths of the coefficient times the throughput of the path with the weight gathered along the ray (section
        // 5.1.2), or the largest coefficient over the wavelengths (section 5.1.1; "spectralprobabilities" "maximum")
        protected Float SpectralMeasure(SampledSpectrum w, SampledSpectrum mu)
        {
            Float measure = 0;
            if (medium.volumeIntegrator.spectralMaximum)
            {
                for (int i = 0; i < NSpectrumSamples; ++i)
                {
                    measure = std.max(measure, std.abs(mu[i]));
                }
                return measure;
            }
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                measure += std.abs(m_ctx.throughput[i] * w[i] * mu[i]);
            }
            return measure / NSpectrumSamples;
        }

        // Spectral tracking along the ray (Kutz et al. 2017, section 5, algorithm 4). The collisions are those of one majorant
        // for every wavelength, the largest over the wavelengths, so the transmittance of the majorant is the same for all
        // and falls out. At a collision the events (a scattering, a null collision and, with "spectralabsorption", an
        // absorption that ends the walk; without it the reduced termination of section 5.1.3) are chosen in proportion to
        // SpectralMeasure(), and the weight of the event taken is mu / (mu_bar P), by wavelength. What the medium emits is
        // added to L at every collision by its expected value, w mu_a Le / mu_bar. No wavelength samples for the others: the
        // densities r of the path are left as they are.
        //
        // With "tracker" "decomposition" this is the combination of section 5.2 (weighted decomposition tracking, algorithm
        // 3, by wavelength): the control component is the smallest density of the majorant segment with the smallest
        // absorption and scattering coefficients over the wavelengths, so it is the same for every wavelength. A collision is
        // the control component's with the probability mu_c / mu_bar, without a lookup: an absorption, which ends the walk,
        // or a scattering of weight one. The others are collisions of the residual, with the probabilities above over what
        // is left of the coefficients.
        //
        // Returns the weight w of the walk, with that of the scattering if there is one, its point and its phase function.
        protected WalkEnd SpectralWalk(VolumeServices rs, Ray wi, Float distance, out SampledSpectrum w, out Point3f point, out PhaseFunction phase,
                                       ref SampledSpectrum L)
        {
            VolumeIntegrator settings = medium.volumeIntegrator;
            SampledWavelengths lambda = m_ctx.lambda;
            w = new SampledSpectrum(1f);
            point = default;
            phase = default;
            Float rMean = m_ctx.r.Average();
            MediumProperties unit = default;
            bool decompose = settings.tracker is DecompositionTracker && medium.ControlProperties(lambda, out unit);
            int widest = 0;
            SampledSpectrum unit_t = new SampledSpectrum(0f);
            if (decompose)
            {
                unit_t = unit.sigma_a + unit.sigma_s;
                for (int i = 1; i < NSpectrumSamples; ++i)
                {
                    if (unit_t[i] > unit_t[widest])
                    {
                        widest = i;
                    }
                }
            }
            RayMajorantIterator iter = medium.SampleRay(wi, distance, lambda, new ScratchBuffer());
            while (true)
            {
                pstd.optional<RayMajorantSegment> seg = iter.Next();
                if (!seg)
                {
                    break;
                }
                RayMajorantSegment sg = seg.value();
                Float majorant = sg.sigma_maj.MaxComponentValue();
                if (!(majorant > 0))
                {
                    continue;
                }
                Float controlA = 0, controlS = 0;
                if (decompose && sg.controlFraction > 0 && unit_t[widest] > 0)
                {
                    Float density = sg.controlFraction * sg.sigma_maj[widest] / unit_t[widest];
                    controlA = density * unit.sigma_a.MinComponentValue();
                    controlS = density * unit.sigma_s.MinComponentValue();
                }
                Float pControl = (controlA + controlS) / majorant;
                Float t = sg.tMin;
                while (true)
                {
                    t += -std.log(1 - rs.GenerateRandomNumber()) / majorant;
                    if (!(t < sg.tMax))
                    {
                        break;
                    }
                    point = wi.Apply(t);
                    if (pControl > 0 && rs.GenerateRandomNumber() < pControl)
                    {
                        // A collision of the control component: no lookup, the same for every wavelength
                        if (rs.GenerateRandomNumber() * (controlA + controlS) < controlA)
                        {
                            return WalkEnd.Ended;
                        }
                        phase = unit.phase;
                        return WalkEnd.Scattered;
                    }
                    MediumProperties mp = medium.SamplePoint(point, lambda, wi.time);
                    Float residual = majorant * (1 - pControl);
                    if ((bool)mp.Le)
                    {
                        L += m_ctx.emissionWeight * w * mp.sigma_a * mp.Le / (residual * rMean);
                    }
                    SampledSpectrum sigma_a = mp.sigma_a - new SampledSpectrum(controlA), sigma_s = mp.sigma_s - new SampledSpectrum(controlS);
                    SampledSpectrum sigma_n = new SampledSpectrum(majorant) - mp.sigma_a - mp.sigma_s;
                    Float absorb = settings.spectralAbsorption ? SpectralMeasure(w, sigma_a) : 0;
                    Float scatter = SpectralMeasure(w, sigma_s), pass = SpectralMeasure(w, sigma_n);
                    Float total = absorb + scatter + pass;
                    if (!(total > 0))
                    {
                        return WalkEnd.Ended;
                    }
                    Float x = rs.GenerateRandomNumber() * total;
                    if (x < absorb)
                    {
                        return WalkEnd.Ended;
                    }
                    if (x < absorb + scatter)
                    {
                        w *= sigma_s * (total / (residual * scatter));
                        phase = mp.phase;
                        return WalkEnd.Scattered;
                    }
                    w *= sigma_n * (total / (residual * pass));
                    if (!(bool)w)
                    {
                        return WalkEnd.Ended;
                    }
                }
            }
            return WalkEnd.Left;
        }

        // The multiplier of the scattering coefficient for the shadow rays of a multiple scattering volume (section 3.7,
        // Wrenninge et al. 2013 and Wrenninge 2015): "contrast" to the power of the depth of the path
        protected Float Contrast()
        {
            Float contrast = medium.volumeIntegrator.contrast;
            return contrast == 1 || m_ctx.depth <= 0 ? 1 : std.pow(contrast, (Float)m_ctx.depth);
        }

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
            SampledSpectrum T_maj = medium.volumeIntegrator.tracker.ForTransmittance().SampleT_maj(ray, medium, tMax, rs.GenerateRandomNumber(), ref rng, lambda,
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

        // Track() for the extinction sigma_a + scale sigma_s: ratio tracking with the tracker of the medium (its majorant bounds
        // the reduced extinction too)
        protected SampledSpectrum TrackScaled(VolumeServices rs, Ray ray, Float tMax, Float scale, ref SampledSpectrum r)
        {
            if (scale == 1)
            {
                return Track(rs, ray, tMax, false, ref r);
            }
            SampledSpectrum T_ray = new SampledSpectrum(1f), rl = new SampledSpectrum(1f), ru = new SampledSpectrum(1f);
            RNG rng = rs.rng;
            SampledSpectrum T_maj = medium.volumeIntegrator.tracker.ForTransmittance().SampleT_maj(ray, medium, tMax, rs.GenerateRandomNumber(), ref rng, m_ctx.lambda,
                (Point3f p, MediumProperties mp, SampledSpectrum sigma_maj, SampledSpectrum T_maj_) =>
                {
                    MediumEvents.RatioTrackingStep(ref T_ray, ref rl, ref ru, T_maj_, sigma_maj, ClampZero(sigma_maj - mp.sigma_a - scale * mp.sigma_s));
                    return (bool)T_ray;
                });
            rs.rng = rng;
            if (!T_ray)
            {
                return T_ray;
            }
            MediumEvents.FinishSegment(ref T_ray, ref rl, ref ru, T_maj);
            r *= rl;
            return T_ray;
        }

        // The emission along the ray up to tMax, attenuated by the extinction of the medium (or, with `absorptionOnly`, by its
        // absorption alone): at every collision of the majorant, the transmittance so far (a ratio-tracking product) times
        // sigma_a Le over the density of the collision, by wavelength
        protected SampledSpectrum TrackEmission(VolumeServices rs, Ray ray, Float tMax, bool absorptionOnly)
        {
            SampledSpectrum sum = new SampledSpectrum(0f);
            if (!medium.IsEmissive())
            {
                return sum;
            }
            if (medium.volumeIntegrator.emissionResidual && !absorptionOnly)
            {
                return TrackEmissionResidual(rs, ray, tMax);
            }
            // (the densities of the path that reaches the ray go on along it: EmissionOver)
            SampledSpectrum T = new SampledSpectrum(1f), re = m_ctx.emissionDensity;
            m_ctx.emissionDensityUsed = true;
            RNG rng = rs.rng;
            medium.volumeIntegrator.tracker.SampleT_maj(ray, medium, tMax, rs.GenerateRandomNumber(), ref rng, m_ctx.lambda,
                (Point3f p, MediumProperties mp, SampledSpectrum sigma_maj, SampledSpectrum T_maj) =>
                {
                    Float pdf = sigma_maj[0] * T_maj[0];
                    SampledSpectrum numerator = T * T_maj / pdf, density = re * T_maj * sigma_maj / pdf;
                    if ((bool)density && (bool)mp.Le)
                    {
                        sum += numerator * mp.sigma_a * mp.Le / density.Average();
                    }
                    SampledSpectrum sigma_n = absorptionOnly ? ClampZero(sigma_maj - mp.sigma_a) : ClampZero(sigma_maj - mp.sigma_a - mp.sigma_s);
                    T *= T_maj * sigma_n / pdf;
                    re *= T_maj * sigma_maj / pdf;
                    return (bool)T;
                });
            rs.rng = rng;
            return sum;
        }

        // The same about a control ("string emission" "residual"; not in the papers: the control of residual ratio tracking,
        // Novak et al. 2014, given to the emission ratio tracking of Huang et al. 2021). Ratio tracking multiplies the
        // transmittance by 1 - sigma_t / majorant at every collision: in dense smoke the product is a few numbers near zero,
        // and what lies behind is found rarely. Here the extinction of each cell of the medium is split into its mean, whose
        // transmittance is an exponential, and the rest, which is ratio tracked: the factor of a collision is
        // 1 - (sigma_t - mean) / majorant, near one where the medium is near its mean. The collisions are those of the
        // largest majorant over the wavelengths and every wavelength has its own factors, so no wavelength samples for the
        // others. The walk is ended by chance once little gets through.
        protected SampledSpectrum TrackEmissionResidual(VolumeServices rs, Ray ray, Float tMax)
        {
            const Float little = 0.02f;
            SampledSpectrum sum = new SampledSpectrum(0f), T = new SampledSpectrum(1f);
            SampledWavelengths lambda = m_ctx.lambda;
            tMax *= Length(ray.d);
            ray.d = Normalize(ray.d);
            RNG rng = rs.rng;
            RayMajorantIterator iter = medium.SampleRay(ray, tMax, lambda, new ScratchBuffer());
            bool ended = false;
            while (!ended)
            {
                pstd.optional<RayMajorantSegment> seg = iter.Next();
                if (!seg)
                {
                    break;
                }
                RayMajorantSegment sg = seg.value();
                Float largest = sg.sigma_maj.MaxComponentValue();
                if (!(largest > 0) || !(sg.tMax > sg.tMin))
                {
                    continue;
                }
                SampledSpectrum control = sg.averageFraction * sg.sigma_maj;
                Float t = sg.tMin;
                while (true)
                {
                    Float step = -std.log(1 - rng.Uniform<Float>()) / largest;
                    if (!(t + step < sg.tMax))
                    {
                        T *= Exp(-(sg.tMax - t) * control);
                        break;
                    }
                    t += step;
                    T *= Exp(-step * control);
                    MediumProperties mp = medium.SamplePoint(ray.Apply(t), lambda, ray.time);
                    if ((bool)mp.Le)
                    {
                        sum += T * mp.sigma_a * mp.Le / largest;
                    }
                    T *= ClampZero(new SampledSpectrum(1f) - (mp.sigma_a + mp.sigma_s - control) / largest);
                    Float most = T.MaxComponentValue();
                    if (most < little)
                    {
                        Float survive = most / little;
                        if (!(most > 0) || !(rng.Uniform<Float>() < survive))
                        {
                            ended = true;
                            break;
                        }
                        T /= survive;
                    }
                }
            }
            rs.rng = rng;
            return sum;
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
            if (medium is AggregateMedium aggregate)
            {
                // (section 6: the aggregate integrates itself)
                return new AggregateVolume(ctx, aggregate);
            }
            if (medium is RefractiveMedium refractive)
            {
                return new RefractiveVolume(ctx, refractive);
            }
            bool homogeneous = medium is HomogeneousMedium;
            switch (medium.volumeIntegrator.scattering)
            {
            case VolumeScattering.None:
                return new EmptyVolume(ctx, medium);
            case VolumeScattering.SinglePDF:
                return new SingleScatterPDFVolume(ctx, medium);
            case VolumeScattering.Absorption:
                return homogeneous ? new BeersLawVolume(ctx, medium) : new BeersLawHeterogeneousVolume(ctx, medium);
            case VolumeScattering.Single:
                return homogeneous ? new SingleScatterHomogeneousVolume(ctx, medium) : new SingleScatterHeterogeneousVolume(ctx, medium);
            default:
                return homogeneous ? new MultiScatterHomogeneousVolume(ctx, medium) : new MultiScatterHeterogeneousVolume(ctx, medium);
            }
        }
    }

    // EmptyVolume Definition (the course, listing 2): the next hit, nothing gathered, nothing lost
    public sealed unsafe class EmptyVolume : Volume
    {
        public EmptyVolume(VolumeContext ctx, Medium medium) : base(ctx, medium) { }

        public override bool Integrate(VolumeServices rs, Ray wi, out SampledSpectrum L, out SampledSpectrum transmittance, out SampledSpectrum weight,
                                       out Point3f P, out Ray wo, out VolumeGeometry g)
        {
            Float distance;
            bool hit = rs.GetNearestHit(wi, out P, out distance, out g);
            L = new SampledSpectrum(0f);
            transmittance = new SampledSpectrum(1f);
            weight = new SampledSpectrum(1f);
            wo = new Ray(P, wi.d, wi.time);
            return hit;
        }

        public override SampledSpectrum Transmittance(VolumeServices rs, Point3f P0, Point3f P1)
        {
            return new SampledSpectrum(1f);
        }

        public override SampledSpectrum Emission(VolumeServices rs, Point3f P0, Point3f P1)
        {
            return new SampledSpectrum(0f);
        }
    }

    // SingleScatterPDFVolume Definition (the course, listing 13, section 3.6.2). The ray is marched with a fixed step. At each
    // step the transmittance so far, the scattering coefficient and an approximation of the light that arrives (a sample of
    // the lights without its shadow ray) make one value of a density along the ray; the point of the scattering is drawn from
    // that density and lit there, with the shadow ray. The transmittance, of Integrate() and of Transmittance(), is the
    // quadrature of the same march, and so is the emission (section 3.8). As the course says of it, the march is biased: the
    // volume is taken as constant over a step. Homogeneous or not, the medium is asked at every step. A ray that does not
    // end is marched over the extent that the medium says it has (Medium.AggregateRegion), or, in a homogeneous medium, as far
    // as anything gets through; any other medium without an extent is then tracked for its transmittance and scatters nothing.
    public sealed unsafe class SingleScatterPDFVolume : Volume
    {
        public SingleScatterPDFVolume(VolumeContext ctx, Medium medium) : base(ctx, medium) { }

        private int Steps(Float length, out Float step)
        {
            int n = Math.Max(1, Math.Min(MaxSteps, (int)std.ceil(length / medium.volumeIntegrator.stepSize)));
            step = length / n;
            return n;
        }

        public override bool Integrate(VolumeServices rs, Ray wi, out SampledSpectrum L, out SampledSpectrum transmittance, out SampledSpectrum weight,
                                       out Point3f P, out Ray wo, out VolumeGeometry g)
        {
            Float distance;
            bool hit = rs.GetNearestHit(wi, out P, out distance, out g);
            L = new SampledSpectrum(0f);
            transmittance = new SampledSpectrum(1f);
            weight = new SampledSpectrum(1f);
            wo = new Ray(P, wi.d, wi.time);
            Float t0, t1;
            if (!Span(wi, distance, out t0, out t1))
            {
                SampledSpectrum r = m_ctx.r;
                transmittance = Track(rs, wi, distance, false, ref r);
                m_ctx.r = r;
                return hit;
            }
            if (!(t1 > t0))
            {
                return hit;
            }
            Float step;
            int n = Steps(t1 - t0, out step);
            SampledWavelengths lambda = m_ctx.lambda;
            SampledSpectrum accumulated = new SampledSpectrum(1f), emitted = new SampledSpectrum(0f);
            SampledSpectrum[] before = new SampledSpectrum[n], extinction = new SampledSpectrum[n];
            Float[] cdf = new Float[n];
            Float sum = 0;
            Point3f saved = m_ctx.P;
            for (int i = 0; i < n; ++i)
            {
                // Update the shading context
                Point3f point = wi.Apply(t0 + (i + 0.5f) * step);
                m_ctx.SetP(point);
                MediumProperties mp = medium.SamplePoint(point, lambda, wi.time);
                SampledSpectrum sigma_t = mp.sigma_a + mp.sigma_s;
                before[i] = accumulated;
                extinction[i] = sigma_t;
                SampledSpectrum middle = accumulated * Attenuation(sigma_t, 0.5f * step);
                // The emission, by quadrature
                if ((bool)mp.Le)
                {
                    emitted += middle * mp.sigma_a * mp.Le * step;
                }
                // T(t) sigma_s L_s for the density, the light without its transmittance
                Float value = 0;
                if (m_ctx.mayScatter && (bool)mp.sigma_s)
                {
                    MediumInteraction at = new MediumInteraction(point, -wi.d, wi.time, medium, mp.phase);
                    Vector3f towards;
                    SampledSpectrum arriving;
                    if (rs.GenerateLightSample(m_ctx, at, out towards, out arriving))
                    {
                        value = (middle * mp.sigma_s * arriving * mp.phase.p(-wi.d, towards)).Average();
                    }
                }
                sum += std.max<Float>(value, 0);
                cdf[i] = sum;
                // Transmittance over the step, accumulated
                accumulated *= Attenuation(sigma_t, step);
            }
            transmittance = accumulated;
            L = m_ctx.emissionWeight * emitted / m_ctx.r.Average();
            m_ctx.SetP(saved);
            if (!m_ctx.mayScatter)
            {
                return hit;
            }
            // The points that are lit. The march is made once for all of them (section 4.1); with "distancesampling" the
            // equiangular density about a point of a light is used instead of the marched density or next to it, and the
            // transmittance of the point comes from the march all the same (section 4.2.4, the decoupled ray march)
            VolumeIntegrator settings = medium.volumeIntegrator;
            bool marchUsable = sum > 0 && !IsInf(sum);
            for (int k = 0; k < settings.lightSamples; ++k)
            {
                EquiangularDensity about = default;
                VolumeLight picked;
                if (settings.distanceSampling != VolumeDistanceSampling.Density && rs.ChooseLight(m_ctx, out picked) && picked.hasPoint)
                {
                    about = EquiangularDensity.Create(wi, t0, t1, picked.point);
                }
                if (!about.valid && !marchUsable)
                {
                    continue;
                }
                Float pe = about.valid ? (settings.distanceSampling == VolumeDistanceSampling.MIS && marchUsable ? 0.5f : 1) : 0;
                bool equiangular = pe == 1 || (pe > 0 && rs.GenerateRandomNumber() < pe);
                int chosen = 0;
                Float within;
                if (equiangular)
                {
                    Float at = (about.Sample(rs.GenerateRandomNumber()) - t0) / step;
                    if (!(at >= 0) || !(at < n))
                    {
                        continue;
                    }
                    chosen = Math.Min((int)at, n - 1);
                    within = Clamp(at - chosen, 0, OneMinusEpsilon);
                }
                else
                {
                    // Sample the CDF
                    Float xi = rs.GenerateRandomNumber() * sum;
                    while (chosen < n - 1 && !(xi < cdf[chosen]))
                    {
                        ++chosen;
                    }
                    Float lower = chosen > 0 ? cdf[chosen - 1] : 0, part = cdf[chosen] - lower;
                    within = part > 0 ? Clamp((xi - lower) / part, 0, OneMinusEpsilon) : 0.5f;
                }
                Float t = t0 + (chosen + within) * step;
                Float density = pe * about.PDF(t);
                if (marchUsable && pe < 1)
                {
                    density += (1 - pe) * (cdf[chosen] - (chosen > 0 ? cdf[chosen - 1] : 0)) / (sum * step);
                }
                if (!(density > 0))
                {
                    continue;
                }
                // Direct lighting at the scatter location
                Point3f point = wi.Apply(t);
                MediumProperties mp = medium.SamplePoint(point, lambda, wi.time);
                if (!(bool)mp.sigma_s)
                {
                    continue;
                }
                SampledSpectrum T = before[chosen] * Attenuation(extinction[chosen], within * step);
                MediumInteraction intr = new MediumInteraction(point, -wi.d, wi.time, medium, mp.phase);
                L += DirectLighting(rs, intr, mp.phase, mp.sigma_s * T / (density * settings.lightSamples), m_ctx.r);
            }
            for (int k = 0; k < settings.lightSamples; ++k)
            {
                L += DoubleScattering(rs, wi, distance) / settings.lightSamples;
            }
            return hit;
        }

        public override SampledSpectrum Transmittance(VolumeServices rs, Point3f P0, Point3f P1)
        {
            Float distance;
            Ray ray = Between(P0, P1, m_ctx.time, out distance);
            Float t0, t1;
            if (!Span(ray, distance, out t0, out t1))
            {
                SampledSpectrum r = m_ctx.r;
                SampledSpectrum tracked = Track(rs, ray, distance, false, ref r);
                m_ctx.r = r;
                return tracked;
            }
            SampledSpectrum accumulated = new SampledSpectrum(1f);
            if (!(t1 > t0))
            {
                return accumulated;
            }
            Float step;
            int n = Steps(t1 - t0, out step);
            for (int i = 0; i < n; ++i)
            {
                MediumProperties mp = medium.SamplePoint(ray.Apply(t0 + (i + 0.5f) * step), m_ctx.lambda, ray.time);
                accumulated *= Attenuation(mp.sigma_a + mp.sigma_s, step);
            }
            return accumulated;
        }

        // (section 3.8: the quadrature of the same march)
        public override SampledSpectrum Emission(VolumeServices rs, Point3f P0, Point3f P1)
        {
            Float distance;
            Ray ray = Between(P0, P1, m_ctx.time, out distance);
            Float t0, t1;
            if (!Span(ray, distance, out t0, out t1))
            {
                return TrackEmission(rs, ray, distance, false);
            }
            SampledSpectrum emitted = new SampledSpectrum(0f);
            if (!(t1 > t0) || !medium.IsEmissive())
            {
                return emitted;
            }
            SampledSpectrum accumulated = new SampledSpectrum(1f);
            Float step;
            int n = Steps(t1 - t0, out step);
            for (int i = 0; i < n; ++i)
            {
                MediumProperties mp = medium.SamplePoint(ray.Apply(t0 + (i + 0.5f) * step), m_ctx.lambda, ray.time);
                SampledSpectrum sigma_t = mp.sigma_a + mp.sigma_s;
                if ((bool)mp.Le)
                {
                    emitted += accumulated * Attenuation(sigma_t, 0.5f * step) * mp.sigma_a * mp.Le * step;
                }
                accumulated *= Attenuation(sigma_t, step);
            }
            return emitted;
        }

        private const int MaxSteps = 4096;
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
            L = m_ctx.emissionWeight * Emitted(mp, mp.sigma_a, distance) / m_ctx.r.Average();
            weight = new SampledSpectrum(1f);
            wo = new Ray(P, wi.d, wi.time);
            return hit;
        }

        public override SampledSpectrum Transmittance(VolumeServices rs, Point3f P0, Point3f P1)
        {
            MediumProperties mp = medium.SamplePoint(P0, m_ctx.lambda, m_ctx.time);
            return Attenuation(mp.sigma_a, Distance(P0, P1));
        }

        public override SampledSpectrum Emission(VolumeServices rs, Point3f P0, Point3f P1)
        {
            MediumProperties mp = medium.SamplePoint(P0, m_ctx.lambda, m_ctx.time);
            return Emitted(mp, mp.sigma_a, Distance(P0, P1));
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
                L = m_ctx.emissionWeight * sum;
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

        public override SampledSpectrum Emission(VolumeServices rs, Point3f P0, Point3f P1)
        {
            Float distance;
            Ray ray = Between(P0, P1, m_ctx.time, out distance);
            return TrackEmission(rs, ray, distance, true);
        }
    }

    // SingleScatterHomogeneousVolume Definition (the course, listing "single scattering in a homogeneous volume", with the
    // sampling of section 4.2): points of the segment are chosen and lit; the beam itself goes on to where the ray leaves, with
    // the transmittance of the segment. A point is chosen with the density of the transmittance, normalised over the segment,
    // with the equiangular density about a point of a light, or with one of the two and the weight of both
    // ("distancesampling"); "lightsamples" points are lit.
    public sealed unsafe class SingleScatterHomogeneousVolume : Volume
    {
        public SingleScatterHomogeneousVolume(VolumeContext ctx, Medium medium) : base(ctx, medium) { }

        // sigma exp(-sigma t) / (1 - exp(-sigma d)), by wavelength
        private static SampledSpectrum TransmittanceDensity(SampledSpectrum sigma_t, Float distance, Float t)
        {
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
            return density;
        }

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
            L = m_ctx.emissionWeight * Emitted(mp, sigma_t, distance) / m_ctx.r.Average();
            if (!(bool)mp.sigma_s || !(distance > 0) || !m_ctx.mayScatter)
            {
                return hit;
            }
            VolumeIntegrator settings = medium.volumeIntegrator;
            Float s0 = sigma_t[0];
            bool densityUsable = s0 > 0 || !IsInf(distance);
            for (int k = 0; k < settings.lightSamples; ++k)
            {
                EquiangularDensity about = default;
                VolumeLight chosen;
                if (settings.distanceSampling != VolumeDistanceSampling.Density && rs.ChooseLight(m_ctx, out chosen) && chosen.hasPoint)
                {
                    about = EquiangularDensity.Create(wi, 0, distance, chosen.point);
                }
                if (!about.valid && !densityUsable)
                {
                    continue;
                }
                // The probability of the equiangular strategy
                Float pe = about.valid ? (settings.distanceSampling == VolumeDistanceSampling.MIS && densityUsable ? 0.5f : 1) : 0;
                bool equiangular = pe == 1 || (pe > 0 && rs.GenerateRandomNumber() < pe);
                Float xi = rs.GenerateRandomNumber();
                Float t;
                if (equiangular)
                {
                    t = about.Sample(xi);
                }
                else if (s0 == 0)
                {
                    t = xi * distance;
                }
                else
                {
                    // The distance of the scattering: sigma exp(-sigma t) / (1 - exp(-sigma d)) for the first wavelength
                    Float span = IsInf(distance) ? 1 : 1 - std.exp(-s0 * distance);
                    t = -std.log(1 - xi * span) / s0;
                }
                if (!(t >= 0) || !(t < distance) || IsInf(t))
                {
                    continue;
                }
                SampledSpectrum density = new SampledSpectrum(pe * about.PDF(t));
                if (pe < 1)
                {
                    density += (1 - pe) * TransmittanceDensity(sigma_t, distance, t);
                }
                if (!(density[0] > 0))
                {
                    continue;
                }
                MediumInteraction intr = new MediumInteraction(wi.Apply(t), -wi.d, wi.time, medium, mp.phase);
                SampledSpectrum numerator = mp.sigma_s * Attenuation(sigma_t, t) / (density[0] * settings.lightSamples);
                L += DirectLighting(rs, intr, mp.phase, numerator, m_ctx.r * density / density[0]);
            }
            for (int k = 0; k < settings.lightSamples; ++k)
            {
                L += DoubleScattering(rs, wi, distance) / settings.lightSamples;
            }
            return hit;
        }

        public override SampledSpectrum Transmittance(VolumeServices rs, Point3f P0, Point3f P1)
        {
            MediumProperties mp = medium.SamplePoint(P0, m_ctx.lambda, m_ctx.time);
            return Attenuation(mp.sigma_a + mp.sigma_s, Distance(P0, P1));
        }

        public override SampledSpectrum Emission(VolumeServices rs, Point3f P0, Point3f P1)
        {
            MediumProperties mp = medium.SamplePoint(P0, m_ctx.lambda, m_ctx.time);
            return Emitted(mp, mp.sigma_a + mp.sigma_s, Distance(P0, P1));
        }
    }

    // SingleScatterHeterogeneousVolume Definition: the point is the first real collision of a delta-tracking walk (there may
    // be none), and the transmittance of the beam is a separate estimate.
    //
    // With "distancesampling" "equiangular" the point is drawn from the equiangular density p_o about a point of a light and
    // its transmittance is a ratio-tracking estimate T~: T~ sigma_s L_s / p_o. With "mis" both points are taken (the course,
    // listing 18, whose first weight has its arguments the wrong way round; the density of the tracked point is T sigma_t,
    // and its T cancels):
    //     sigma_s L_s / (sigma_t + p_o)      at the tracked point,
    //     T~ sigma_s L_s / (sigma_t + p_o)   at the other,
    // whose expectations add up to the integral of T sigma_s L_s. Where no light gives a point (a light at infinity), "mis" takes
    // the uniform density over the segment for p_o (section 4.2.1), and "equiangular" tracks. The sigma_t of the two weights is
    // the mean over the wavelengths (the course's channel average): the weights then do not depend on which wavelength
    // tracked, add up to one at every point all the same, and each estimate keeps its own weighing over the wavelengths.
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
            VolumeIntegrator settings = medium.volumeIntegrator;
            int samples = m_ctx.mayScatter ? settings.lightSamples : 1;
            bool emissive = medium.IsEmissive();
            L = new SampledSpectrum(0f);
            for (int k = 0; k < samples; ++k)
            {
                // The strategy other than tracking
                EquiangularDensity about = default;
                bool uniform = false;
                if (m_ctx.mayScatter && settings.distanceSampling != VolumeDistanceSampling.Density)
                {
                    VolumeLight chosen;
                    if (rs.ChooseLight(m_ctx, out chosen) && chosen.hasPoint)
                    {
                        about = EquiangularDensity.Create(wi, 0, distance, chosen.point);
                    }
                    if (!about.valid && settings.distanceSampling == VolumeDistanceSampling.MIS && distance > 0 && !IsInf(distance))
                    {
                        uniform = true;
                    }
                }
                bool other = about.valid || uniform;
                bool tracked = !other || settings.distanceSampling == VolumeDistanceSampling.MIS;

                if (settings.spectralTracking && (tracked || (k == 0 && emissive)))
                {
                    // Spectral tracking (Volume.SpectralWalk) to the point: its weight is that of the walk with the scattering
                    SampledSpectrum w, gathered = new SampledSpectrum(0f);
                    Point3f point;
                    PhaseFunction phase;
                    WalkEnd end = SpectralWalk(rs, wi, distance, out w, out point, out phase, ref gathered);
                    if (k == 0)
                    {
                        L += gathered;
                    }
                    if (tracked && end == WalkEnd.Scattered && m_ctx.mayScatter)
                    {
                        MediumInteraction intr = new MediumInteraction(point, -wi.d, wi.time, medium, phase);
                        if (!other)
                        {
                            L += DirectLighting(rs, intr, phase, w / samples, m_ctx.r);
                        }
                        else
                        {
                            // (the weight against the other point is a function of the point: a collision of the control
                            // component, made without a lookup, needs one for it)
                            MediumProperties mp = medium.SamplePoint(point, lambda, wi.time);
                            Float t = Dot(point - wi.o, wi.d);
                            Float po = about.valid ? about.PDF(t) : 1 / distance;
                            Float mean = (mp.sigma_a + mp.sigma_s).Average();
                            L += DirectLighting(rs, intr, phase, w * (mean / ((mean + po) * samples)), m_ctx.r);
                        }
                    }
                }
                else if (tracked || (k == 0 && emissive))
                {
                    SampledSpectrum numerator = new SampledSpectrum(1f), density = m_ctx.r, emitted = new SampledSpectrum(0f);
                    bool found = false;
                    Point3f point = default;
                    MediumProperties at = default;
                    RNG rng = rs.rng;
                    bool first = k == 0;
                    medium.volumeIntegrator.tracker.SampleT_maj(wi, medium, distance, rs.GenerateRandomNumber(), ref rng, lambda,
                        (Point3f p, MediumProperties mp, SampledSpectrum sigma_maj, SampledSpectrum T_maj) =>
                        {
                            Float pdfCollision = sigma_maj[0] * T_maj[0];
                            if (first && (bool)mp.Le)
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
                            // (a null collision that only the rounding of the probabilities chose, where the majorant is the
                            // extinction itself: nothing happened)
                            if (!(pdfNull > 0))
                            {
                                return true;
                            }
                            numerator *= T_maj * sigma_n / pdfNull;
                            density *= T_maj * sigma_n / pdfNull;
                            return (bool)numerator && (bool)density;
                        });
                    rs.rng = rng;
                    L += m_ctx.emissionWeight * emitted;
                    if (tracked && found && m_ctx.mayScatter && (bool)numerator && (bool)density)
                    {
                        MediumInteraction intr = new MediumInteraction(point, -wi.d, wi.time, medium, at.phase);
                        if (!other)
                        {
                            L += DirectLighting(rs, intr, at.phase, numerator / samples, density);
                        }
                        else
                        {
                            Float t = Dot(point - wi.o, wi.d);
                            Float po = about.valid ? about.PDF(t) : 1 / distance;
                            Float mean = (at.sigma_a + at.sigma_s).Average();
                            L += DirectLighting(rs, intr, at.phase, numerator * (mean / ((mean + po) * samples)), density);
                        }
                    }
                }

                if (other)
                {
                    Float xi = rs.GenerateRandomNumber();
                    Float t = about.valid ? about.Sample(xi) : xi * distance;
                    Float po = about.valid ? about.PDF(t) : 1 / distance;
                    if (t >= 0 && t < distance && !IsInf(t) && po > 0)
                    {
                        Point3f point = wi.Apply(t);
                        MediumProperties mp = medium.SamplePoint(point, lambda, wi.time);
                        if ((bool)mp.sigma_s)
                        {
                            SampledSpectrum rTo = m_ctx.r;
                            SampledSpectrum T = Track(rs, wi, t, false, ref rTo);
                            if ((bool)T && (bool)rTo)
                            {
                                Float denominator = tracked ? (mp.sigma_a + mp.sigma_s).Average() + po : po;
                                MediumInteraction intr = new MediumInteraction(point, -wi.d, wi.time, medium, mp.phase);
                                L += DirectLighting(rs, intr, mp.phase, T * mp.sigma_s / (denominator * samples), rTo);
                            }
                        }
                    }
                }
            }
            for (int k = 0; k < samples; ++k)
            {
                L += DoubleScattering(rs, wi, distance) / samples;
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

        public override SampledSpectrum Emission(VolumeServices rs, Point3f P0, Point3f P1)
        {
            Float distance;
            Ray ray = Between(P0, P1, m_ctx.time, out distance);
            return TrackEmission(rs, ray, distance, false);
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
            // The equiangular point about a point of a light, with its share of the direct light (EquiangularShare)
            bool weighed = medium.volumeIntegrator.distanceSampling != VolumeDistanceSampling.Density && m_ctx.mayScatter && distance > 0 && !m_ctx.polarised;
            if (weighed)
            {
                VolumeLight chosen;
                EquiangularDensity about = default;
                if (rs.ChooseLight(m_ctx, out chosen) && chosen.hasPoint)
                {
                    about = EquiangularDensity.Create(wi, 0, distance, chosen.point);
                }
                if (about.valid)
                {
                    Float te = about.Sample(rs.GenerateRandomNumber());
                    if (te >= 0 && te < distance && !IsInf(te))
                    {
                        SampledSpectrum toPoint = Attenuation(sigma_t, te);
                        L += EquiangularShare(rs, wi, te, about.PDF(te), mp, toPoint, sigma_t * toPoint, m_ctx.r, chosen);
                    }
                }
            }
            // Two more scatterings drawn jointly about a light at a position, with their share (JointShare)
            if (distance > 0)
            {
                L += JointShare(rs, wi, distance, true);
            }
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
                    L += m_ctx.emissionWeight * T * mp.sigma_a * mp.Le / pdf / density.Average();
                }
                weight = T * mp.sigma_s / pdf;
                m_ctx.r = density;
                P = wi.Apply(t);
                g = new VolumeGeometry();
                g.isSurface = false;
                g.phase = mp.phase;
                g.scatter = new MediumInteraction(P, -wi.d, wi.time, medium, mp.phase);
                if (weighed || m_ctx.joint != null || m_ctx.jointOnCurve != null)
                {
                    g.direct = CollisionWeight(wi, distance, t, sigma_t * T);
                }
                g.joint = JointAt(wi, distance, t, sigma_t * T, mp.phase);
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

        // (in a medium that is the same everywhere the transmittance is known, and is what a path finds: W)
        protected override bool Reach(VolumeServices rs, Ray ray, Float t, out SampledSpectrum through, out SampledSpectrum densities, out SampledSpectrum nulls)
        {
            MediumProperties mp = medium.SamplePoint(ray.o, m_ctx.lambda, ray.time);
            through = Attenuation(mp.sigma_a + mp.sigma_s, t);
            densities = new SampledSpectrum(1f);
            nulls = through;
            return (bool)through;
        }

        public override SampledSpectrum Transmittance(VolumeServices rs, Point3f P0, Point3f P1)
        {
            MediumProperties mp = medium.SamplePoint(P0, m_ctx.lambda, m_ctx.time);
            return Attenuation(mp.sigma_a + Contrast() * mp.sigma_s, Distance(P0, P1));
        }

        public override SampledSpectrum Emission(VolumeServices rs, Point3f P0, Point3f P1)
        {
            MediumProperties mp = medium.SamplePoint(P0, m_ctx.lambda, m_ctx.time);
            return Emitted(mp, mp.sigma_a + mp.sigma_s, Distance(P0, P1));
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
            if (medium.volumeIntegrator.spectralTracking)
            {
                return IntegrateSpectral(rs, wi, out L, out transmittance, out weight, out P, out wo, out g);
            }
            Float distance;
            Point3f exit;
            VolumeGeometry surface;
            bool hit = rs.GetNearestHit(wi, out exit, out distance, out surface);
            transmittance = new SampledSpectrum(1f);
            SampledWavelengths lambda = m_ctx.lambda;
            SampledSpectrum beta = new SampledSpectrum(1f), r = m_ctx.r, emitted = new SampledSpectrum(0f);
            // The equiangular point about a point of a light, with its share of the direct light (EquiangularShare). Its null
            // collisions are those of a ratio-tracking walk with the tracker of the medium; with the decomposition tracker
            // the collisions of the two walks are not of one kind, and the weights leave W out (they still add up to one).
            bool pathWeights = !(medium.volumeIntegrator.tracker is DecompositionTracker);
            bool weighed = medium.volumeIntegrator.distanceSampling != VolumeDistanceSampling.Density && m_ctx.mayScatter && distance > 0 && !m_ctx.polarised;
            if (weighed)
            {
                VolumeLight chosen;
                EquiangularDensity about = default;
                if (rs.ChooseLight(m_ctx, out chosen) && chosen.hasPoint)
                {
                    about = EquiangularDensity.Create(wi, 0, distance, chosen.point);
                }
                if (about.valid)
                {
                    Float te = about.Sample(rs.GenerateRandomNumber());
                    if (te >= 0 && te < distance && !IsInf(te))
                    {
                        SampledSpectrum T_ray = new SampledSpectrum(1f), rl = new SampledSpectrum(1f), ru = new SampledSpectrum(1f), nulls = new SampledSpectrum(1f);
                        RNG walk = rs.rng;
                        SampledSpectrum T_last = medium.volumeIntegrator.tracker.ForTransmittance().SampleT_maj(wi, medium, te, rs.GenerateRandomNumber(), ref walk, lambda,
                            (Point3f p, MediumProperties mp, SampledSpectrum sigma_maj, SampledSpectrum T_maj) =>
                            {
                                SampledSpectrum sigma_n = ClampZero(sigma_maj - mp.sigma_a - mp.sigma_s);
                                MediumEvents.RatioTrackingStep(ref T_ray, ref rl, ref ru, T_maj, sigma_maj, sigma_n);
                                for (int i = 0; i < NSpectrumSamples; ++i)
                                {
                                    if (sigma_maj[i] > 0)
                                    {
                                        nulls[i] *= sigma_n[i] / sigma_maj[i];
                                    }
                                }
                                return (bool)T_ray;
                            });
                        rs.rng = walk;
                        if ((bool)T_ray)
                        {
                            MediumEvents.FinishSegment(ref T_ray, ref rl, ref ru, T_last);
                            MediumProperties there = medium.SamplePoint(wi.Apply(te), lambda, wi.time);
                            SampledSpectrum reached = (there.sigma_a + there.sigma_s) * (pathWeights ? nulls : new SampledSpectrum(1f));
                            emitted += EquiangularShare(rs, wi, te, about.PDF(te), there, T_ray, reached, m_ctx.r * rl, chosen);
                        }
                    }
                }
            }
            // Two more scatterings drawn jointly about a light at a position, with their share (JointShare)
            if (distance > 0)
            {
                emitted += JointShare(rs, wi, distance, pathWeights);
            }
            SampledSpectrum nullsToCollision = new SampledSpectrum(1f);
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
                            emitted += m_ctx.emissionWeight * beta * T_maj / pdfCollision * mp.sigma_a * mp.Le / de.Average();
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
                    // (a null collision that only the rounding of the probabilities chose, where the majorant is the extinction
                    // itself: nothing happened)
                    if (!(pdfNull > 0))
                    {
                        return true;
                    }
                    beta *= T_maj * sigma_n / pdfNull;
                    r *= T_maj * sigma_n / pdfNull;
                    for (int i = 0; i < NSpectrumSamples; ++i)
                    {
                        if (sigma_maj[i] > 0)
                        {
                            nullsToCollision[i] *= sigma_n[i] / sigma_maj[i];
                        }
                    }
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
                SampledSpectrum product = (at.sigma_a + at.sigma_s) * (pathWeights ? nullsToCollision : new SampledSpectrum(1f));
                if (weighed || m_ctx.joint != null || m_ctx.jointOnCurve != null)
                {
                    g.direct = CollisionWeight(wi, distance, Dot(point - wi.o, wi.d), product);
                }
                g.joint = JointAt(wi, distance, Dot(point - wi.o, wi.d), product, at.phase);
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

        // Spectral tracking (Volume.SpectralWalk) in the place of the walk above
        private bool IntegrateSpectral(VolumeServices rs, Ray wi, out SampledSpectrum L, out SampledSpectrum transmittance, out SampledSpectrum weight,
                                       out Point3f P, out Ray wo, out VolumeGeometry g)
        {
            Float distance;
            bool hit = rs.GetNearestHit(wi, out P, out distance, out g);
            transmittance = new SampledSpectrum(1f);
            L = new SampledSpectrum(0f);
            wo = new Ray(P, wi.d, wi.time);
            Point3f point;
            PhaseFunction phase;
            WalkEnd end = SpectralWalk(rs, wi, distance, out weight, out point, out phase, ref L);
            if (end == WalkEnd.Ended)
            {
                weight = new SampledSpectrum(0f);
                return false;
            }
            if (end == WalkEnd.Scattered)
            {
                P = point;
                g = new VolumeGeometry();
                g.isSurface = false;
                g.phase = phase;
                g.scatter = new MediumInteraction(point, -wi.d, wi.time, medium, phase);
                wo = new Ray(point, wi.d, wi.time);
                return true;
            }
            return hit;
        }

        public override SampledSpectrum Transmittance(VolumeServices rs, Point3f P0, Point3f P1)
        {
            Float distance;
            Ray ray = Between(P0, P1, m_ctx.time, out distance);
            SampledSpectrum r = m_ctx.r;
            SampledSpectrum T = TrackScaled(rs, ray, distance, Contrast(), ref r);
            m_ctx.r = r;
            return T;
        }

        public override SampledSpectrum Emission(VolumeServices rs, Point3f P0, Point3f P1)
        {
            Float distance;
            Ray ray = Between(P0, P1, m_ctx.time, out distance);
            return TrackEmission(rs, ray, distance, false);
        }
    }
}
