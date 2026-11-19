// (not in pbrt-v4) skydomelight.cs
//
// What the lights of the analytic sky models have in common (HosekSkyLight: hoseksky.cs; PragueSkyLight: praguesky.cs): an
// infinite light of the radiance of a model above the horizon, with the solar disc, a ground below the horizon if it is
// asked for, the sampling of its directions, and the polarisation of the sky as the integrators ask for it.
//
// The frame of the sky has z to the zenith and the sun in the half plane of x and z with x positive. A model gives, for a
// direction above the horizon, the radiance of the sky and of the solar disc at a wavelength, and it names the wavebands
// that the tables here are made by (for the luminance that the directions are sampled by, and for the light that falls
// on the ground).
//
// The radiance is in W m^-2 sr^-1 nm^-1 times `scale` (683 times the "scale" of the scene: the units of the thermal
// emitters, so that the Y of the film is cd/m^2).
//
// Sampling. A direction is drawn from the solar disc (uniformly) or from the sky, the two as a mixture by their shares of
// the light; the density of a direction is that of the mixture, so the light is found by its own samples and by the
// samples of the surfaces alike. The sky is sampled by a table of its luminance over the sphere, 128 by 128 (the
// equal-area square of pbrt's image lights, with a floor so that no direction is left out), unless a model has a way of
// its own (SkyDensity, SampleSky).

using System;
using PLight = pbrt.PolarisedLightSystem.Light;

namespace pbrt
{
    public abstract unsafe class SkyDomeLight : LightBase
    {
        // `zenith`, `sunDirection`: render space, unit length; `sun`: with the solar disc; `ground`: with the ground below
        // the horizon. The tables are made by Initialise, which the constructor of a model calls when the model is ready.
        protected SkyDomeLight(Vector3f zenith, Vector3f sunDirection, Float scale, bool sun, bool ground)
            : base(LightType.Infinite, new Transform(), new MediumInterface())
        {
            this.zenith = zenith;
            this.sunDirection = sunDirection;
            this.scale = scale;
            this.sun = sun;
            this.ground = ground;
            solarElevation = Math.Asin(Math.Min(Math.Max((double)pbrt.Dot(zenith, sunDirection), -1.0), 1.0));
            // (x toward the sun)
            frame = pbrt.AbsDot(zenith, sunDirection) < 0.9999f
                ? Frame.FromXZ(pbrt.Normalize(sunDirection - pbrt.Dot(zenith, sunDirection) * zenith), zenith) : Frame.FromZ(zenith);
        }

        // The wavebands of the model that the film can find: their number, a wavelength that stands for each, and their
        // width (nm)
        protected abstract int BandCount { get; }

        protected abstract double BandWavelength(int band);

        protected abstract double BandWidth { get; }

        // The radiance of the sky dome and that of the solar disc alone (nothing outside the disc), for a direction above the
        // horizon: `local` in the frame of the sky, at the angle theta from the zenith and gamma from the sun
        protected abstract void Evaluate(Vector3f local, double theta, double gamma, double wavelength, out double sky, out double solar);

        // The angular radius of the solar disc
        protected abstract double SolarRadius { get; }

        // The two wavebands that a wavelength lies between and the weight of the second, as the model takes its wavelengths
        // (`low` negative: outside the model)
        protected abstract void BandsAt(double wavelength, out int low, out int high, out double weightHigh);

        // The degree of linear polarisation of the sky dome in a direction, positive across the plane of the sun and the
        // direction and negative in it; and whether the sky is polarised at all
        protected virtual double DegreeAt(Vector3f local, double theta, double gamma, double wavelength)
        {
            return 0;
        }

        protected virtual bool IsPolarised
        {
            get { return false; }
        }

        // The density of a direction of the sky (the sphere without the solar disc) per solid angle, and a direction by it:
        // the table
        protected virtual double SkyDensity(Vector3f local)
        {
            return distribution.PDF(pbrt.EqualAreaSphereToSquare(local)) / (4 * Math.PI);
        }

        protected virtual Vector3f SampleSky(Point2f u)
        {
            return pbrt.EqualAreaSquareToSphere(distribution.Sample(u));
        }

        // Makes the tables: of the luminance over the sphere, of the light on the ground, and the shares of the sky and the
        // disc. `albedo`: of the ground, by waveband. `table`: false if the model samples its sky in its own way.
        protected void Initialise(double[] albedo, bool table)
        {
            int bands = BandCount;
            bandLuminance = new double[bands];
            for (int band = 0; band < bands; ++band)
            {
                // (the luminous efficiency at the waveband times its width)
                bandLuminance[band] = BandWidth * Spectra.Y().Evaluate((Float)BandWavelength(band));
            }
            cosSolarRadius = Math.Cos(SolarRadius);
            sunSolidAngle = 2 * Math.PI * (1 - cosSolarRadius);

            const int resolution = 128, sub = 2;
            Float[] luminance = new Float[resolution * resolution];
            double[] irradiance = new double[bands];
            double cell = 4 * Math.PI / (resolution * resolution * sub * sub);
            double below = 0;
            for (int pass = 0; pass < 2; ++pass)
            {
                if (pass == 1)
                {
                    // (the sun on the level ground, by rings of its disc)
                    for (int band = 0; band < bands && sun; ++band)
                    {
                        irradiance[band] += DiscIntegral(BandWavelength(band), true);
                    }
                    if (!ground)
                    {
                        break;
                    }
                    groundRadiance = new double[bands];
                    for (int band = 0; band < bands; ++band)
                    {
                        groundRadiance[band] = albedo[band] * irradiance[band] / Math.PI;
                    }
                }
                below = 0;
                for (int v = 0; v < resolution; ++v)
                {
                    for (int u = 0; u < resolution; ++u)
                    {
                        double sum = 0;
                        for (int s = 0; s < sub * sub; ++s)
                        {
                            Point2f uv = new Point2f((u + (s % sub + 0.5f) / sub) / resolution, (v + (s / sub + 0.5f) / sub) / resolution);
                            Vector3f local = pbrt.EqualAreaSquareToSphere(uv);
                            for (int band = 0; band < bands; ++band)
                            {
                                double value = BandRadiance(band, local);
                                sum += value * bandLuminance[band];
                                if (pass == 0 && local.z > 0)
                                {
                                    irradiance[band] += value * local.z * cell;
                                }
                            }
                            if (local.z < 0)
                            {
                                below += sum / (sub * sub);
                            }
                        }
                        luminance[v * resolution + u] = (Float)(sum / (sub * sub));
                    }
                }
            }
            double total = 0;
            foreach (Float value in luminance)
            {
                total += value;
            }
            double mean = total / luminance.Length;
            groundShare = groundRadiance != null && total > 0 ? Math.Min(Math.Max(below / total, 0.02), 0.9) : 0;
            if (table)
            {
                // (a floor, so that no direction of the sphere is left without a density)
                for (int i = 0; i < luminance.Length; ++i)
                {
                    luminance[i] = (Float)(luminance[i] + 0.02 * mean);
                }
                distribution = new PiecewiseConstant2D(new pstd.span<Float>(luminance), resolution, resolution,
                                                       new Bounds2f(new Point2f(0, 0), new Point2f(1, 1)));
            }

            // The shares of the sky and of the disc in the light, by their luminance over the sphere
            double skyLuminance = mean * 4 * Math.PI, sunLuminance = 0;
            for (int band = 0; band < bands && sun; ++band)
            {
                sunLuminance += DiscIntegral(BandWavelength(band), false) * bandLuminance[band];
            }
            sunShare = sun && sunLuminance > 0 ? Math.Min(Math.Max(sunLuminance / (sunLuminance + skyLuminance), 0.05), 0.95) : 0;
            totalLuminance = skyLuminance + sunLuminance;
            darkBelow = true;
            for (int band = 0; band < bands && groundRadiance != null; ++band)
            {
                darkBelow &= !(groundRadiance[band] > 0);
            }
        }

        // The integral of the radiance of the solar disc over it, by rings of equal solid angle about its centre (the
        // radiance at each ring is taken on the side of the disc toward the zenith and on the other, and at its middle
        // height); `level`: times the cosine at a level surface
        private double DiscIntegral(double wavelength, bool level)
        {
            if (!(solarElevation > -SolarRadius))
            {
                return 0;
            }
            const int rings = 16;
            double sum = 0;
            for (int ring = 0; ring < rings; ++ring)
            {
                double gamma = Math.Acos(1 - (ring + 0.5) / rings * (1 - cosSolarRadius));
                for (int side = -1; side <= 1; ++side)
                {
                    double elevation = solarElevation + side * gamma;
                    double weight = side == 0 ? 0.5 : 0.25;
                    if (!(elevation > 0))
                    {
                        continue;
                    }
                    // (a point of the disc: in the sun's vertical for the two sides, beside the sun for the middle)
                    Vector3f local = side == 0
                        ? new Vector3f((Float)(Math.Cos(solarElevation) * Math.Cos(gamma)), (Float)Math.Sin(gamma), (Float)(Math.Sin(solarElevation) * Math.Cos(gamma)))
                        : new Vector3f((Float)Math.Cos(elevation), 0, (Float)Math.Sin(elevation));
                    double theta = Math.Acos(Math.Min(Math.Max((double)local.z, -1.0), 1.0));
                    if (!(local.z > 0))
                    {
                        continue;
                    }
                    Evaluate(local, theta, gamma, wavelength, out double sky, out double solar);
                    sum += weight * Math.Max(solar, 0.0) * sunSolidAngle / rings * (level ? local.z : 1.0);
                }
            }
            return sum;
        }

        // The radiance of one waveband of the sky, or of the ground, in a direction of the frame of the sky (without the
        // solar disc)
        private double BandRadiance(int band, Vector3f local)
        {
            if (!(local.z > 0))
            {
                return groundRadiance != null ? groundRadiance[band] : 0;
            }
            Evaluate(local, Math.Acos(Math.Min((double)local.z, 1.0)), GammaLocal(local), BandWavelength(band), out double sky, out double solar);
            return Math.Max(sky, 0.0);
        }

        // The angle of a direction from the sun, accurate where it is small
        protected double Gamma(Vector3f w)
        {
            Vector3f difference = w - sunDirection;
            return 2 * Math.Asin(Math.Min(0.5 * Math.Sqrt((double)pbrt.LengthSquared(difference)), 1.0));
        }

        private double GammaLocal(Vector3f local)
        {
            double x = local.x - Math.Cos(solarElevation), z = local.z - Math.Sin(solarElevation);
            return 2 * Math.Asin(Math.Min(0.5 * Math.Sqrt(x * x + (double)local.y * local.y + z * z), 1.0));
        }

        // The radiance in a direction (render space, unit length)
        private SampledSpectrum Radiance(Vector3f w, SampledWavelengths lambda)
        {
            SampledSpectrum L = new SampledSpectrum(0.0f);
            Vector3f local = frame.ToLocal(w);
            double cosTheta = pbrt.Dot(w, zenith);
            double gamma = Gamma(w);
            double theta = Math.Acos(Math.Min(Math.Max(cosTheta, -1.0), 1.0));
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                double value = 0;
                if (!(cosTheta > 0))
                {
                    if (groundRadiance != null)
                    {
                        BandsAt(lambda[i], out int low, out int high, out double weightHigh);
                        if (low >= 0)
                        {
                            value = (1 - weightHigh) * groundRadiance[low] + (high >= 0 ? weightHigh * groundRadiance[high] : 0);
                        }
                    }
                }
                else
                {
                    Evaluate(local, theta, gamma, lambda[i], out double sky, out double solar);
                    value = Math.Max(sky, 0.0);
                    if (sun && gamma < SolarRadius)
                    {
                        value += Math.Max(solar, 0.0);
                    }
                }
                L[i] = (Float)(scale * value);
            }
            return L;
        }

        // The Stokes vector of the light of the sky that travels along w (render space, unit length), of the intensity
        // `scalar`: the light of the dome polarised across or in the plane of the sun and the direction, the rest not
        public PLight Polarised(Vector3f w, SampledWavelengths lambda, SampledSpectrum scalar)
        {
            Vector3f toSky = -w;
            double cosTheta = pbrt.Dot(toSky, zenith);
            if (!IsPolarised || !(cosTheta > 0))
            {
                return new PLight(scalar);
            }
            double gamma = Gamma(toSky);
            double theta = Math.Acos(Math.Min(cosTheta, 1.0));
            Vector3f across = pbrt.Cross(toSky, sunDirection);
            if (!(pbrt.LengthSquared(across) > 1e-12f))
            {
                return new PLight(scalar);
            }
            Vector3f local = frame.ToLocal(toSky);
            SampledSpectrum first = new SampledSpectrum(0.0f);
            bool inDisc = sun && gamma < SolarRadius;
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                double degree = Math.Min(Math.Max(DegreeAt(local, theta, gamma, lambda[i]), -1.0), 1.0);
                if (inDisc && degree != 0)
                {
                    // (in front of the disc: the share of the dome in the light)
                    Evaluate(local, theta, gamma, lambda[i], out double sky, out double solar);
                    sky = Math.Max(sky, 0.0);
                    solar = Math.Max(solar, 0.0);
                    degree = sky + solar > 0 ? degree * sky / (sky + solar) : 0;
                }
                first[i] = (Float)degree * scalar[i];
            }
            bool any = false;
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                any |= first[i] != 0;
            }
            if (!any)
            {
                return new PLight(scalar);
            }
            StokesVector stokes = new StokesVector(scalar, first, new SampledSpectrum(0.0f), new SampledSpectrum(0.0f));
            return new PLight(stokes).InDirection(w, pbrt.Normalize(across));
        }

        public override SampledSpectrum Le(Ray ray, SampledWavelengths lambda)
        {
            return Radiance(pbrt.Normalize(ray.d), lambda);
        }

        // The density of a direction, per solid angle. Without light below the horizon no sample is spent there: a
        // direction of the mixture that falls below is mirrored in the horizon, and the density above is that of the
        // direction and of its mirror image. (A light that gives directions without light also biases bdpt, which leaves
        // out its light samples whenever the walk from the light did not start.)
        private Float Density(Vector3f w)
        {
            if (!darkBelow)
            {
                return (Float)MixtureDensity(w);
            }
            Float cosTheta = pbrt.Dot(w, zenith);
            if (!(cosTheta > 0))
            {
                return 0;
            }
            return (Float)(MixtureDensity(w) + MixtureDensity(w - 2 * cosTheta * zenith));
        }

        private Vector3f SampleDirection(Point2f u)
        {
            Vector3f w = SampleMixture(u);
            if (darkBelow)
            {
                Float cosTheta = pbrt.Dot(w, zenith);
                if (cosTheta < 0)
                {
                    w -= 2 * cosTheta * zenith;
                }
            }
            return w;
        }

        // The mixture of the disc and of the sky over the sphere
        private double MixtureDensity(Vector3f w)
        {
            double density = (1 - sunShare) * SkyDensity(frame.ToLocal(w));
            if (sunShare > 0 && Gamma(w) < SolarRadius)
            {
                density += sunShare / sunSolidAngle;
            }
            return density;
        }

        private Vector3f SampleMixture(Point2f u)
        {
            if (u[0] < sunShare)
            {
                Point2f remapped = new Point2f((Float)Math.Min(u[0] / sunShare, (double)FloatOneMinusEpsilon), u[1]);
                return Frame.FromZ(sunDirection).FromLocal(pbrt.SampleUniformCone(remapped, (Float)cosSolarRadius));
            }
            Point2f rest = new Point2f((Float)Math.Min((u[0] - sunShare) / (1 - sunShare), (double)FloatOneMinusEpsilon), u[1]);
            return frame.FromLocal(SampleSky(rest));
        }

        public override pstd.optional<LightLiSample> SampleLi(LightSampleContext ctx, Point2f u, SampledWavelengths lambda,
                                                              bool allowIncompletePDF = false)
        {
            Vector3f wi = pbrt.Normalize(SampleDirection(u));
            Float pdf = Density(wi);
            if (!(pdf > 0))
            {
                return default;
            }
            SampledSpectrum L = Radiance(wi, lambda);
            if (!L)
            {
                return default;
            }
            return new LightLiSample(L, wi, pdf, new Interaction(ctx.p() + wi * (2 * sceneRadius), (MediumInterface)null));
        }

        public override Float PDF_Li(LightSampleContext ctx, Vector3f w, bool allowIncompletePDF = false)
        {
            return Density(pbrt.Normalize(w));
        }

        public override pstd.optional<LightLeSample> SampleLe(Point2f u1, Point2f u2, ref SampledWavelengths lambda, Float time)
        {
            Vector3f toSky = pbrt.Normalize(SampleDirection(u1));
            Float pdfDir = Density(toSky);
            if (!(pdfDir > 0))
            {
                return default;
            }
            Frame wFrame = Frame.FromZ(toSky);
            Point2f cd = pbrt.SampleUniformDiskConcentric(u2);
            Point3f pDisk = sceneCenter + sceneRadius * wFrame.FromLocal(new Vector3f(cd.x, cd.y, 0));
            Ray ray = new Ray(pDisk + sceneRadius * toSky, -toSky, time);
            return new LightLeSample(Radiance(toSky, lambda), ray, 1 / (Pi * pbrt.Sqr(sceneRadius)), pdfDir);
        }

        public override void PDF_Le(Ray ray, Float* pdfPos, Float* pdfDir)
        {
            *pdfPos = 1 / (Pi * pbrt.Sqr(sceneRadius));
            *pdfDir = Density(pbrt.Normalize(-ray.d));
        }

        // The constant spectrum of the luminance of the light over the sphere, on the disc of the scene
        public override SampledSpectrum Phi(SampledWavelengths lambda)
        {
            return new SampledSpectrum((Float)(scale * totalLuminance / CIE_Y_integral * Math.PI * sceneRadius * sceneRadius));
        }

        public override pstd.optional<LightBounds> Bounds()
        {
            return default;
        }

        public override void Preprocess(Bounds3f sceneBounds)
        {
            Point3f c;
            Float r;
            sceneBounds.BoundingSphere(&c, &r);
            sceneCenter = c;
            sceneRadius = r;
        }

        // "up" and "sundir" of a scene in render space; the sun may be as far below the horizon as `lowest` (radians)
        protected static void Directions(Transform renderFromLight, ParameterDictionary parameters, FileLoc loc, string name, double lowest,
                                         out Vector3f zenith, out Vector3f toSun)
        {
            Vector3f up = parameters.GetOneVector3f("up", new Vector3f(0, 0, 1));
            Vector3f sunDirection = parameters.GetOneVector3f("sundir", new Vector3f(0, 0, 1));
            if (pbrt.LengthSquared(up) == 0 || pbrt.LengthSquared(sunDirection) == 0)
            {
                pbrt.ErrorExit(loc, pbrt.StringPrintf("\"%s\": \"up\" and \"sundir\" must not be zero.", name));
            }
            zenith = pbrt.Normalize(renderFromLight.Apply(up));
            toSun = pbrt.Normalize(renderFromLight.Apply(sunDirection));
            if (Math.Asin(Math.Min(Math.Max((double)pbrt.Dot(zenith, toSun), -1.0), 1.0)) < lowest)
            {
                pbrt.ErrorExit(loc, pbrt.StringPrintf("\"%s\": the sun is lower than the model allows (%f degrees).", name,
                                                      (Float)(lowest * 180 / Math.PI)));
            }
        }

        protected readonly Vector3f zenith, sunDirection;
        protected readonly Frame frame;
        protected readonly Float scale;
        protected readonly bool sun, ground;
        // (of the sun above the horizon, radians)
        protected readonly double solarElevation;
        // (the share of the ground in the light of the sphere without the disc)
        protected double groundShare;
        // No light comes from below the horizon
        private bool darkBelow;
        private double[] bandLuminance, groundRadiance;
        private PiecewiseConstant2D distribution;
        private double cosSolarRadius, sunSolidAngle, sunShare, totalLuminance;
        private Point3f sceneCenter;
        private Float sceneRadius;
    }
}
