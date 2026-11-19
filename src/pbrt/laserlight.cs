// (not in pbrt-v4) laserlight.cs
//
// A laser: the fundamental mode of a beam (TEM00), times a beam quality factor.
//
//   LightSource "laser" "point3 from" [ 0 0 0 ] "point3 to" [ 0 0 1 ] "float power" 0.005 "float wavelength" 532
//       "float waist" 0.0005
//
// What emits. A Gaussian beam of the waist radius w0 (where the irradiance has fallen to 1/e^2 of that on the axis) and
// of the far-field half angle theta0 is, as radiance, a Gaussian in the position and in the direction: at its waist
//
//   L(x, s) = L0 exp(-2 |x|^2 / w0^2) exp(-2 |s|^2 / theta0^2) / cos^4(theta),     w0 theta0 = M^2 lambda / pi
//
// with x the point in the plane of the waist, s the slope of the ray against the axis (the tangent of its angle theta, as
// a vector in that plane) and L0 = 4 P / (pi^2 w0^2 theta0^2) = 4 P / (M^2 lambda)^2. This is the Wigner distribution of
// the mode, which is positive everywhere: rays that carry it give the beam of the wave theory exactly at every distance,
//
//   E(rho, z) = 2 P / (pi w(z)^2) exp(-2 rho^2 / w(z)^2),      w(z)^2 = w0^2 + z^2 theta0^2
//
// (z from the waist, rho from the axis), the divergence included. The division by cos^4 is what makes the power P and
// the irradiance E exact rather than paraxial; at the angles of a laser it is one to a part in a million. The beam is
// emitted from the plane through "from", across the axis, toward "to"; the waist lies "waistdistance" beyond that plane
// (0: at the plane; a beam that is focused in front of the laser has it positive).
//
// The spectrum is one line: a Gaussian around "wavelength" of the full width at half maximum "linewidth". The film is
// told of the line (SpectralLineSampling) and takes "wavelengthfraction" of its wavelengths from it.
//
// The light is polarised if "polarisation" says so: "linear", with the electric field along "polarisationaxis" (taken at
// right angles to the beam), or "circular". "polarisationdegree" is the polarised part of the power.
//
// How bright. "power" is the radiant power of the beam in watts. The radiance has the units of the thermal emitters:
// W m^-2 sr^-1 nm^-1 times 683, so that the Y of the film is cd/m^2. "metersperunit" is the size of a scene unit.
//
// How it is rendered. Everything is in closed form and sampled exactly:
//   * toward a point (SampleLi): the rays through the point are a product of two Gaussians of the slope, which is one
//     Gaussian. A slope is drawn from it; the radiance over its density is E / cos(theta), the same for every sample.
//   * from the light (SampleLe): the point at the waist and the slope are two independent Gaussians; the weight of every
//     ray is the power.
// The Gaussians end at six times their radii (w0 for the point at the waist, theta0 for the slope), where e^-72 of the
// peak is left: the light then has an edge, a cone around the beam that the light samplers are given as its bounds, and
// a point outside it is never asked for the light. Within the edge the beam is the Gaussian beam to 10^-31 of its power.
//
// The radiance is finite, but no ray finds the light: there is no surface to hit. The integrators are told so in the way
// they know, by the type of a light at a position, and a light of that type is sampled by its own routines only; what a
// path would find by chance is nothing that is left out, since no path can.
//
// Limits: the modes above the fundamental are only in M^2 (the beam is wider in angle and stays Gaussian); no speckle and
// no interference, as nowhere else in the renderer; the divergence is that of the centre of the line. A beam that is seen
// by way of mirrors or lenses needs "bdpt" or "lightpath", as with every narrow light.

using System;
using PLight = pbrt.PolarisedLightSystem.Light;

namespace pbrt
{
    // The beam and its sampling, for the lights that differ in what the beam carries: LaserLight (one spectral line) and
    // LaserLight2 (any spectrum, or an image across the beam: laserlight2.cs)
    public abstract unsafe class GaussianBeamLight : LightBase
    {
        // `waist`, `waistDistance`: scene units; `divergence`: the far-field half angle (radians); `stokes1` to `stokes3`:
        // the Stokes vector over the intensity, in the frame whose x is `axis` (render space)
        protected GaussianBeamLight(Transform renderFromLight, MediumInterface mediumInterface, double waist, double divergence,
                                    double waistDistance, double stokes1, double stokes2, double stokes3, Vector3f axis)
            : base(LightType.DeltaPosition, renderFromLight, mediumInterface)
        {
            this.waist = waist;
            this.divergence = divergence;
            this.waistDistance = waistDistance;
            this.stokes1 = stokes1;
            this.stokes2 = stokes2;
            this.stokes3 = stokes3;
            this.axis = axis;
            sourceWidthSquared = waist * waist + waistDistance * waistDistance * divergence * divergence;
        }

        // The power of the beam at the wavelengths of a path, as the rays through a point of the plane of the light carry
        // it (the radiance of a ray is this times the Gaussians of unit power)
        protected abstract SampledSpectrum Emitted(SampledWavelengths lambda, double sourceX, double sourceY);

        // The constant spectrum that has the luminance of the beam: what the light samplers weigh this light by
        protected abstract Float Luminous();

        // Where the Gaussians end, in their radii
        private const double Edge = 6;

        // Whether a ray of the light, by its point at the waist and its slope, is within the edge
        private bool Within(double waistX, double waistY, double slopeX, double slopeY)
        {
            return waistX * waistX + waistY * waistY <= Edge * Edge * waist * waist &&
                   slopeX * slopeX + slopeY * slopeY <= Edge * Edge * divergence * divergence;
        }

        // A light at a position that is one point (a point or spot light): what equiangular sampling and the pivots of the
        // production volumes take such a light for. The points of a laser are many.
        public static bool IsPointLight(Light light)
        {
            return light != null && light.Type() == LightType.DeltaPosition && !(light is GaussianBeamLight);
        }

        // The square of the beam radius at the distance z from the plane of the light
        private double WidthSquared(double z)
        {
            double fromWaist = z - waistDistance;
            return waist * waist + fromWaist * fromWaist * divergence * divergence;
        }

        // The irradiance of a beam of unit power on a plane across the axis, at a point of the space of the light
        protected double UnitIrradiance(Point3f pLight)
        {
            if (!(pLight.z > 0))
            {
                return 0;
            }
            double widthSquared = WidthSquared(pLight.z);
            double offsetSquared = (double)pLight.x * pLight.x + (double)pLight.y * pLight.y;
            return 2 / (Math.PI * widthSquared) * Math.Exp(-2 * offsetSquared / widthSquared);
        }

        public override pstd.optional<LightLiSample> SampleLi(LightSampleContext ctx, Point2f u, SampledWavelengths lambda,
                                                              bool allowIncompletePDF = false)
        {
            Point3f p = renderFromLight.ApplyInverse(ctx.p());
            double irradiance = UnitIrradiance(p);
            if (!(irradiance > 0))
            {
                return default;
            }
            // The slopes of the rays through the point: exp(-2 |rho - zeta s|^2 / w0^2) exp(-2 |s|^2 / theta0^2), a Gaussian
            // of the mean rho zeta theta0^2 / w^2 and of the variance w0^2 theta0^2 / (4 w^2) per axis
            double fromWaist = p.z - waistDistance;
            double widthSquared = WidthSquared(p.z);
            double variance = waist * waist * divergence * divergence / (4 * widthSquared);
            double shift = fromWaist * divergence * divergence / widthSquared;
            double radius = Math.Sqrt(-2 * Math.Log(1 - (double)u[0]) * variance);
            double angle = 2 * Math.PI * u[1];
            double slopeX = p.x * shift + radius * Math.Cos(angle);
            double slopeY = p.y * shift + radius * Math.Sin(angle);
            double densitySlope = (1 - (double)u[0]) / (2 * Math.PI * variance);
            if (!Within(p.x - fromWaist * slopeX, p.y - fromWaist * slopeY, slopeX, slopeY))
            {
                return default;
            }

            // The point of the plane of the light that the ray left, and the density of its direction
            Point3f source = new Point3f((Float)(p.x - p.z * slopeX), (Float)(p.y - p.z * slopeY), 0);
            double cosTheta = 1 / Math.Sqrt(1 + slopeX * slopeX + slopeY * slopeY);
            double density = densitySlope / (cosTheta * cosTheta * cosTheta);
            Point3f pSource = renderFromLight.Apply(source);
            Vector3f wi = pbrt.Normalize(pSource - ctx.p());
            // (the radiance over the density is E / cos(theta))
            SampledSpectrum L = (Float)(irradiance / cosTheta * density) * Emitted(lambda, source.x, source.y);
            if (!L)
            {
                return default;
            }
            return new LightLiSample(L, wi, (Float)density, new Interaction(pSource, mediumInterface));
        }

        // The density of a distance along a segment of a ray through a medium (render space; `direction` of unit length, the
        // segment from 0 to tMax, which may be infinite), for the scattering points that the beam lights: about the
        // irradiance of the beam along the ray. The square of the distance of the ray from the axis is a quadratic of the
        // distance along it, so with the beam radius of the point nearest the axis the irradiance is a Gaussian: of the
        // centre `centreDistance` and of the standard deviation `spread` (infinite for a ray along the beam). `mass` is the
        // part of the Gaussian on the segment and `lower` its distribution function at the start. A tenth of the samples
        // are uniform over a finite segment, and all of them if next to nothing of the Gaussian is on it: the density is
        // positive on all of the segment, whatever the beam does away from the nearest point.
        private void DistanceDensity(Point3f origin, Vector3f direction, Float tMax, out double centreDistance, out double spread,
                                     out double lower, out double mass, out double uniformPart)
        {
            Point3f o = renderFromLight.ApplyInverse(origin);
            Vector3f d = renderFromLight.ApplyInverse(direction);
            double across = (double)d.x * d.x + (double)d.y * d.y;
            bool finite = !Float.IsInfinity(tMax);
            centreDistance = 0;
            spread = double.PositiveInfinity;
            lower = 0;
            mass = 0;
            uniformPart = finite ? 1 : 0;
            if (!(across > 1e-12))
            {
                return;
            }
            centreDistance = -((double)o.x * d.x + (double)o.y * d.y) / across;
            double nearest = Math.Min(Math.Max(centreDistance, 0.0), finite ? (double)tMax : double.MaxValue);
            double width = Math.Sqrt(WidthSquared(Math.Max(o.z + nearest * d.z, 0.0)));
            spread = width / (2 * Math.Sqrt(across));
            double root2 = 1.4142135623730951;
            lower = 0.5 * (1 + SpectralLineSampling.ErrorFunction(-centreDistance / (spread * root2)));
            double upper = finite ? 0.5 * (1 + SpectralLineSampling.ErrorFunction((tMax - centreDistance) / (spread * root2))) : 1;
            mass = upper - lower;
            if (!(mass > 1e-9))
            {
                mass = 0;
                return;
            }
            uniformPart = finite ? 0.1 : 0;
        }

        private double DistanceDensityAt(double t, Float tMax, double centreDistance, double spread, double mass, double uniformPart)
        {
            double density = uniformPart > 0 ? uniformPart / tMax : 0;
            if (mass > 0)
            {
                density += (1 - uniformPart) * SpectralLineSampling.Gaussian(t, centreDistance, spread) / mass;
            }
            return density;
        }

        // A distance along the segment and its density per unit length. False if there is none to give.
        public bool SampleDistance(Point3f origin, Vector3f direction, Float tMax, Float u, out Float t, out Float pdf)
        {
            t = 0;
            pdf = 0;
            if (!(tMax > 0))
            {
                return false;
            }
            DistanceDensity(origin, direction, tMax, out double centreDistance, out double spread, out double lower, out double mass,
                            out double uniformPart);
            double distance;
            if (u < uniformPart)
            {
                distance = u / uniformPart * tMax;
            }
            else if (mass > 0)
            {
                double inside = (u - uniformPart) / (1 - uniformPart);
                double probability = Math.Min(Math.Max(lower + inside * mass, 1e-300), 1 - 1e-16);
                distance = centreDistance + spread * 1.4142135623730951 * SpectralLineSampling.InverseErrorFunction(2 * probability - 1);
            }
            else
            {
                return false;
            }
            if (!Float.IsInfinity(tMax))
            {
                distance = Math.Min(distance, (double)tMax);
            }
            t = (Float)Math.Max(distance, 0.0);
            pdf = (Float)DistanceDensityAt(t, tMax, centreDistance, spread, mass, uniformPart);
            return pdf > 0 && !Float.IsInfinity(pdf) && !Float.IsNaN(pdf);
        }

        public Float PdfDistance(Point3f origin, Vector3f direction, Float tMax, Float t)
        {
            if (!(tMax > 0) || !(t >= 0) || !(t <= tMax))
            {
                return 0;
            }
            DistanceDensity(origin, direction, tMax, out double centreDistance, out double spread, out double lower, out double mass,
                            out double uniformPart);
            Float pdf = (Float)DistanceDensityAt(t, tMax, centreDistance, spread, mass, uniformPart);
            return Float.IsInfinity(pdf) || Float.IsNaN(pdf) ? 0 : pdf;
        }

        public override Float PDF_Li(LightSampleContext ctx, Vector3f wi, bool allowIncompletePDF = false)
        {
            return 0.0f;
        }

        // The densities of a ray of the light: of its point in the plane of the light (per area), and of its direction
        // given the point (per solid angle). `originX`, `originY`: the point; `slopeX`, `slopeY`: the slope.
        private void Densities(double originX, double originY, double slopeX, double slopeY, out double densityPoint,
                               out double densityDirection)
        {
            // (the point at the waist and the slope are independent)
            double waistX = originX + waistDistance * slopeX, waistY = originY + waistDistance * slopeY;
            densityPoint = 2 / (Math.PI * sourceWidthSquared) *
                           Math.Exp(-2 * (originX * originX + originY * originY) / sourceWidthSquared);
            if (!(densityPoint > 0) || !Within(waistX, waistY, slopeX, slopeY))
            {
                densityPoint = 0;
                densityDirection = 0;
                return;
            }
            double joint = 2 / (Math.PI * waist * waist) * Math.Exp(-2 * (waistX * waistX + waistY * waistY) / (waist * waist)) *
                           2 / (Math.PI * divergence * divergence) *
                           Math.Exp(-2 * (slopeX * slopeX + slopeY * slopeY) / (divergence * divergence));
            double cosTheta = 1 / Math.Sqrt(1 + slopeX * slopeX + slopeY * slopeY);
            densityDirection = joint / densityPoint / (cosTheta * cosTheta * cosTheta);
        }

        public override pstd.optional<LightLeSample> SampleLe(Point2f u1, Point2f u2, ref SampledWavelengths lambda, Float time)
        {
            double radiusPoint = 0.5 * waist * Math.Sqrt(-2 * Math.Log(1 - (double)u1[0]));
            double anglePoint = 2 * Math.PI * u1[1];
            double radiusSlope = 0.5 * divergence * Math.Sqrt(-2 * Math.Log(1 - (double)u2[0]));
            double angleSlope = 2 * Math.PI * u2[1];
            double slopeX = radiusSlope * Math.Cos(angleSlope), slopeY = radiusSlope * Math.Sin(angleSlope);
            // (from the waist back, or on, to the plane of the light)
            double originX = radiusPoint * Math.Cos(anglePoint) - waistDistance * slopeX;
            double originY = radiusPoint * Math.Sin(anglePoint) - waistDistance * slopeY;
            Densities(originX, originY, slopeX, slopeY, out double densityPoint, out double densityDirection);
            if (!(densityPoint > 0) || !(densityDirection > 0))
            {
                return default;
            }
            Vector3f direction = pbrt.Normalize(new Vector3f((Float)slopeX, (Float)slopeY, 1));
            Ray ray = renderFromLight.Apply(new Ray(new Point3f((Float)originX, (Float)originY, 0), direction, time));
            // (the light has no surface, so no cosine is taken at it: this is the radiance times the cosine, and over the
            // two densities it is the power)
            SampledSpectrum L = (Float)(densityPoint * densityDirection) * Emitted(lambda, originX, originY);
            if (!L)
            {
                return default;
            }
            return new LightLeSample(L, ray, (Float)densityPoint, (Float)densityDirection, mediumInterface.outside);
        }

        public override void PDF_Le(Ray ray, Float* pdfPos, Float* pdfDir)
        {
            *pdfPos = 0;
            *pdfDir = 0;
            Vector3f direction = renderFromLight.ApplyInverse(ray.d);
            if (!(direction.z > 0))
            {
                return;
            }
            Point3f origin = renderFromLight.ApplyInverse(ray.o);
            double slopeX = direction.x / direction.z, slopeY = direction.y / direction.z;
            Densities(origin.x - origin.z * slopeX, origin.y - origin.z * slopeY, slopeX, slopeY, out double densityPoint,
                      out double densityDirection);
            *pdfPos = (Float)densityPoint;
            *pdfDir = (Float)densityDirection;
        }

        public override SampledSpectrum Phi(SampledWavelengths lambda)
        {
            return new SampledSpectrum(Luminous());
        }

        // The cone of the rays within the edge: they leave the plane of the light within Edge sqrt(2) beam radii of the axis
        // (the point at the waist and the slope are each within Edge radii) at slopes of Edge theta0 at most. The power is
        // that of a light of the intensity on the axis in every direction, 4 pi times 2 P / (pi theta0^2), as the bounds of a
        // spot light have it: over the square of the distance it is about the irradiance in the beam.
        public override pstd.optional<LightBounds> Bounds()
        {
            Float extent = (Float)(1.5 * Edge * Math.Sqrt(sourceWidthSquared));
            Bounds3f box = new Bounds3f();
            box = pbrt.Union(box, renderFromLight.Apply(new Point3f(-extent, -extent, 0)));
            box = pbrt.Union(box, renderFromLight.Apply(new Point3f(extent, -extent, 0)));
            box = pbrt.Union(box, renderFromLight.Apply(new Point3f(-extent, extent, 0)));
            box = pbrt.Union(box, renderFromLight.Apply(new Point3f(extent, extent, 0)));
            Vector3f w = pbrt.Normalize(renderFromLight.Apply(new Vector3f(0, 0, 1)));
            // (two milliradians more than the cone: the samplers keep the axis and the cosine in sixteen bits)
            double halfAngle = Math.Min(Math.Atan(Edge * divergence) + 0.002, 0.5 * Math.PI);
            Float phi = (Float)(8 * Luminous() / (divergence * divergence));
            return new LightBounds(box, w, phi, 1, (Float)Math.Cos(halfAngle), false);
        }

        // The Stokes vector of the light that travels along w, of the intensity `scalar`
        public PLight Polarised(Vector3f w, SampledSpectrum scalar)
        {
            if (stokes1 == 0 && stokes2 == 0 && stokes3 == 0)
            {
                return new PLight(scalar);
            }
            StokesVector stokes = new StokesVector(scalar, (Float)stokes1 * scalar, (Float)stokes2 * scalar, (Float)stokes3 * scalar);
            return new PLight(stokes).InDirection(w, axis);
        }

        // "from" and "to": the plane of the light is at `from`, across the line to `to`
        protected static Transform Placement(Transform renderFromLight, ParameterDictionary parameters)
        {
            Point3f from = parameters.GetOnePoint3f("from", new Point3f(0, 0, 0));
            Point3f to = parameters.GetOnePoint3f("to", new Point3f(0, 0, 1));
            Transform dirToZ = new Transform(Frame.FromZ(pbrt.Normalize(to - from)));
            Transform t = pbrt.Translate(new Vector3f(from.x, from.y, from.z)) * pbrt.Inverse(dirToZ);
            return renderFromLight * t;
        }

        // "polarisation" and "polarisationdegree" as the Stokes vector over the intensity
        protected static void Polarisation(ParameterDictionary parameters, FileLoc loc, out double stokes1, out double stokes3)
        {
            string polarisation = parameters.GetOneString("polarisation", "none");
            double degree = parameters.GetOneFloat("polarisationdegree", 1.0f);
            if (!(degree >= 0) || !(degree <= 1))
            {
                pbrt.ErrorExit(loc, "\"polarisationdegree\" must be between 0 and 1.");
            }
            stokes1 = 0;
            stokes3 = 0;
            if (polarisation == "linear")
            {
                stokes1 = degree;
            }
            else if (polarisation == "circular")
            {
                stokes3 = degree;
            }
            else if (polarisation != "none")
            {
                pbrt.ErrorExit(loc, pbrt.StringPrintf("%s: polarisation unknown (none, linear, circular).", polarisation));
            }
        }

        protected readonly double waist, divergence, waistDistance, sourceWidthSquared;
        private readonly double stokes1, stokes2, stokes3;
        private readonly Vector3f axis;
    }

    public sealed unsafe class LaserLight : GaussianBeamLight
    {
        // `power`: 683 times the watts, over the square metres of a square scene unit; `centre`, `width`: the line and its
        // standard deviation (nm); the others as GaussianBeamLight has them
        public LaserLight(Transform renderFromLight, MediumInterface mediumInterface, double waist, double divergence,
                          double waistDistance, double power, double centre, double width, double stokes1, double stokes2,
                          double stokes3, Vector3f axis)
            : base(renderFromLight, mediumInterface, waist, divergence, waistDistance, stokes1, stokes2, stokes3, axis)
        {
            this.power = power;
            this.centre = centre;
            this.width = width;
        }

        // The power times the line, per nanometre
        protected override SampledSpectrum Emitted(SampledWavelengths lambda, double sourceX, double sourceY)
        {
            SampledSpectrum value = new SampledSpectrum(0.0f);
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                value[i] = (Float)(power * SpectralLineSampling.Gaussian(lambda[i], centre, width));
            }
            return value;
        }

        // (a few fixed wavelengths never find the line)
        protected override Float Luminous()
        {
            return (Float)(power * Spectra.Y().Evaluate((Float)centre) / CIE_Y_integral);
        }

        // The irradiance of the beam on a plane across the axis, at a point of the space of the light, over the line
        // (in the units of `power`)
        public double Irradiance(Point3f pLight)
        {
            return power * UnitIrradiance(pLight);
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ LaserLight %s waist: %f divergence: %f wavelength: %f ]", BaseToString(), waist, divergence,
                                     centre);
        }

        public static LaserLight Create(Transform renderFromLight, Medium medium, ParameterDictionary parameters, FileLoc loc,
                                        Allocator alloc)
        {
            double metersPerUnit = parameters.GetOneFloat("metersperunit", 1.0f);
            double watts = parameters.GetOneFloat("power", 0.001f) * (double)parameters.GetOneFloat("scale", 1.0f);
            double centre = parameters.GetOneFloat("wavelength", 532.0f);
            double lineWidth = parameters.GetOneFloat("linewidth", 1.0f);
            double waist = parameters.GetOneFloat("waist", (Float)(0.0005 / metersPerUnit));
            double quality = parameters.GetOneFloat("m2", 1.0f);
            double divergence = parameters.GetOneFloat("divergence", 0.0f);
            double waistDistance = parameters.GetOneFloat("waistdistance", 0.0f);
            double fraction = parameters.GetOneFloat("wavelengthfraction", 0.5f);
            Vector3f axis = parameters.GetOneVector3f("polarisationaxis", new Vector3f(0, 0, 1));

            if (!(metersPerUnit > 0) || !(waist > 0) || !(lineWidth > 0) || !(watts >= 0))
            {
                pbrt.ErrorExit(loc, "\"laser\": \"metersperunit\", \"waist\" and \"linewidth\" must be positive, and \"power\" not negative.");
            }
            if (!(centre > 360) || !(centre < 830))
            {
                pbrt.ErrorExit(loc, "\"laser\": \"wavelength\" must be between 360 and 830 nm (the wavelengths of the film).");
            }
            // The half angle of the fundamental mode of this waist, lambda / (pi w0), times the beam quality factor
            double limit = centre * 1e-9 / (Math.PI * waist * metersPerUnit);
            if (divergence > 0)
            {
                if (divergence < limit)
                {
                    pbrt.Warning(loc, pbrt.StringPrintf("\"laser\": a divergence of %f rad is below that of a beam of this waist and " +
                                                        "wavelength (%f rad): no beam is that narrow.", (Float)divergence, (Float)limit));
                }
            }
            else
            {
                if (!(quality >= 1))
                {
                    pbrt.ErrorExit(loc, "\"laser\": \"m2\" is 1 for the ideal beam and above for every other.");
                }
                divergence = quality * limit;
            }
            Polarisation(parameters, loc, out double stokes1, out double stokes3);
            Transform finalRenderFromLight = Placement(renderFromLight, parameters);

            // (the standard deviation of a Gaussian of this full width at half maximum)
            double width = lineWidth / 2.3548200450309493;
            if (watts > 0)
            {
                SpectralLineSampling.Add(new double[] { centre }, new double[] { width }, new double[] { 1.0 }, fraction);
            }
            return new LaserLight(finalRenderFromLight, new MediumInterface(medium), waist, divergence, waistDistance,
                                  683.002 * watts / (metersPerUnit * metersPerUnit), centre, width, stokes1, 0, stokes3,
                                  pbrt.Normalize(renderFromLight.Apply(axis)));
        }

        private readonly double power, centre, width;
    }
}
