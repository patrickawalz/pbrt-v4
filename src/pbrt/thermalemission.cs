// (not in pbrt-v4) thermalemission.cs
//
// The light of a glowing solid: A. Wilkie and A. Weidlich, "A Physically Plausible Model for Light Emission from Glowing
// Solid Objects" (EGSR 2011), approach C. By Kirchhoff's law the surface emits the radiance of a blackbody of its
// temperature times its absorbance, which is what its own material does not send back:
//
//     Le(x, w) = B(T(x)) (1 - integral of f(w, wi) |cos(wi)| dwi)
//
// The integral is what a path that reaches the surface along -w would collect from surroundings of the radiance B, so a
// glowing object in a furnace of its own temperature cannot be told from the furnace. With the Mueller matrix M of the
// scattering the same statement gives the polarisation: the emitted Stokes vector is B (e0 - integral of M e0), e0 being
// unpolarised light, and the light of a smooth surface is polarised at right angles to the light that it reflects.
//
// The interface of a smooth conductor or dielectric has the integral in closed form (the Fresnel reflectance). Any other
// material gives one sample of it: a direction of its BSDF with the weight f |cos| / pdf, whose mean is the integral, so
// the mean of the emission is exact and nothing is tabulated. The random numbers of the sample come from a hash of the
// point and of the direction, as those of the LayeredBxDF of pbrt-v4 do: the radiance of a light is asked for without a
// sampler. The same point and direction give the same sample again, which makes the scalar and the polarised value of
// one evaluation agree.
//
// Light that the material lets through is not absorbed: a lossless glass emits nothing at its surface (what glows is the
// medium inside it). "bool opaque" says that the body behind the interface absorbs all that enters and has the
// temperature of the surface, so what is transmitted counts as absorbed: the emission is B (1 - F), as for the black
// quartz of the paper.
//
// "bool blackbody" leaves the material out: the surface emits B in every direction, unpolarised, on top of what it
// reflects (approach B of the paper, which is not physical: for comparisons).
//
// AreaLightSource "thermal2" is the same light with the point of each light sample resampled by the temperature, for a
// surface whose temperature varies ("integer candidates", 8): DiffuseAreaLight.SampleLiResampled.
//
// Both sides of the surface emit unless "bool twosided" is false: the absorbance belongs to the direction, whichever side
// it is on. The side below a rough dielectric emits into the medium there, n^2 B (1 - ...); the index is known for the
// DielectricBxDF only, and other BxDFs that separate two media of different indices are taken to emit into a vacuum.
//
// The radiance is the spectral radiance of Planck's law in W m^-2 sr^-1 nm^-1 times 683 lm/W: the photometric scale of
// the other lights of pbrt, for which a spectrum whose inner product with the luminous efficiency is one is one nit.
//
// Limits: the optical constants are those of the material as given (they change with the temperature, and most hot
// surfaces oxidise: see the sections 2.3 and 5 of the paper); the light that goes below the surface of a subsurface
// material is counted as not absorbed; and a sample of the reflectance of a BSDF whose weights exceed one gives
// negative values of the radiance (their mean is right).

using System;
using PLight = pbrt.PolarisedLightSystem.Light;
using Attenuation = pbrt.PolarisedLightSystem.Attenuation;

namespace pbrt
{
    // The Stokes vector of emitted light, for every integrator that carries polarisation. An integrator has the radiance of a
    // light as a spectrum, times the factors of its estimator; these give the same quantity as polarised light. Lights are
    // unpolarised except a laser (LaserLight), a sky (HosekSkyLight) and the glowing surfaces (ThermalEmission), whose Stokes vector is taken
    // relative to its intensity:
    // the same point and direction give the same sample of the emission, so the two belong together.
    public static class PolarisedEmission
    {
        // `scalar` is the radiance of the surface toward w times any factors
        public static PLight AtHit(SurfaceInteraction surface, Vector3f w, SampledWavelengths lambda, SampledSpectrum scalar,
                                   bool roughPolarisation = true)
        {
            DiffuseAreaLight area = surface.areaLight as DiffuseAreaLight;
            if (area == null || area.thermal == null)
            {
                return new PLight(scalar);
            }
            return Scaled(area.LPolarised(surface, w, lambda, roughPolarisation), scalar);
        }

        // `scalar` is the radiance of the light from the point toward w times any factors
        public static PLight AtPoint(Light light, Interaction point, Vector3f w, SampledWavelengths lambda, SampledSpectrum scalar,
                                     bool roughPolarisation = true)
        {
            // (the beam of a laser: laserlight.cs)
            GaussianBeamLight laser = light as GaussianBeamLight;
            if (laser != null)
            {
                return laser.Polarised(w, scalar);
            }
            // (the sky: hoseksky.cs)
            HosekSkyLight sky = light as HosekSkyLight;
            if (sky != null)
            {
                return sky.Polarised(w, lambda, scalar);
            }
            DiffuseAreaLight area = light as DiffuseAreaLight;
            if (area == null || area.thermal == null)
            {
                return new PLight(scalar);
            }
            return Scaled(area.LPolarised(point.p(), point.n, point.uv, w, lambda, roughPolarisation), scalar);
        }

        // `scalar` is the radiance of an infinite light for the direction w that its light travels in (toward the scene) times
        // any factors: what a ray that leaves the scene finds
        public static PLight AtInfinity(Light light, Vector3f w, SampledWavelengths lambda, SampledSpectrum scalar)
        {
            HosekSkyLight sky = light as HosekSkyLight;
            if (sky != null)
            {
                return sky.Polarised(pbrt.Normalize(w), lambda, scalar);
            }
            return new PLight(scalar);
        }

        private static PLight Scaled(PLight emitted, SampledSpectrum scalar)
        {
            SampledSpectrum intensity = emitted.GetIntensity();
            SampledSpectrum factor = new SampledSpectrum(0.0f);
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                factor[i] = intensity[i] != 0 ? scalar[i] / intensity[i] : 0;
            }
            return emitted * factor;
        }
    }

    // The light that an integrator sampled, kept for the polarisation of its emission
    public struct SampledEmitter
    {
        public Light light;
        public Interaction point;
    }

    public sealed unsafe class ThermalEmission
    {
        // lm / W at 555 nm
        private const Float LuminousEfficacy = 683.002f;

        private readonly Shape shape;
        private readonly FloatTexture temperature;
        private readonly Float scale;
        private readonly bool opaque;
        private readonly bool blackbody;
        private Material material;

        private bool boundComputed = false;
        private Float boundValue = 0;

        [ThreadStatic]
        private static ScratchBuffer scratchBuffer;

        public ThermalEmission(Shape shape, FloatTexture temperature, Float scale, bool opaque, bool blackbody)
        {
            this.shape = shape;
            this.temperature = temperature;
            this.scale = scale;
            this.opaque = opaque;
            this.blackbody = blackbody;
        }

        public void SetMaterial(Material material)
        {
            this.material = material;
            boundComputed = false;
            if (material != null && material.HasSubsurfaceScattering())
            {
                pbrt.Warning("A \"thermal\" area light on a material with subsurface scattering: the light that enters the " +
                             "surface is counted as not absorbed, and the emission is too weak.");
            }
        }

        // The radiance of a blackbody of the temperature T
        public SampledSpectrum Blackbody(Float T, SampledWavelengths lambda)
        {
            SampledSpectrum B = new SampledSpectrum(0.0f);
            if (!(T > 0))
            {
                return B;
            }
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                // (W m^-2 sr^-1 m^-1 to W m^-2 sr^-1 nm^-1)
                B[i] = scale * LuminousEfficacy * 1e-9f * pbrt.Blackbody(lambda[i], T);
            }
            return B;
        }

        // The candidate points of a light sample ("thermal2": DiffuseAreaLight.SampleLiResampled); one: the plain light
        public int candidates = 1;

        // What a candidate point of a light sample is kept in proportion to: the radiance of a blackbody of its temperature at
        // one wavelength in the middle of the visible range. It does not depend on the wavelengths of the path, so the density
        // of the light for a direction can be given without them (DiffuseAreaLight.PDF_Li).
        public Float Target(Interaction point)
        {
            Float T = new UniversalTextureEvaluator().Apply(temperature, new TextureEvalContext(point));
            return T > 0 ? pbrt.Blackbody(TargetWavelength, T) : 0;
        }

        private const Float TargetWavelength = 580;

        // About what resampling with n candidates makes of the density of a point with the target t, as a factor: a candidate
        // there is kept against n - 1 others of the mean target, n t / (t + (n - 1) mean). One for one candidate, t / mean for
        // many, and never more than n: a point that few candidates find is not found more often than they find it.
        public Float ResampledDensity(Float target)
        {
            Float mean = MeanTarget();
            if (!(mean > 0) || !(target > 0))
            {
                return target > 0 ? 1 : 0;
            }
            return candidates * target / (target + (candidates - 1) * mean);
        }

        // The mean of the target over the area of the shape (256 points), which the density of the resampled light is taken
        // relative to. Any positive value gives weights that add to one; the nearer it is to the mean, the better they are.
        public Float MeanTarget()
        {
            if (!meanTargetComputed)
            {
                lock (this)
                {
                    if (!meanTargetComputed)
                    {
                        const int side = 16;
                        RNG rng = new RNG(0x54454d50UL);
                        double weighted = 0, weights = 0;
                        for (int i = 0; i < side; ++i)
                        {
                            for (int j = 0; j < side; ++j)
                            {
                                Point2f u = new Point2f((i + rng.Uniform<Float>()) / side, (j + rng.Uniform<Float>()) / side);
                                pstd.optional<ShapeSample> ss = shape.Sample(u);
                                if (!ss || !(ss.value().pdf > 0))
                                {
                                    continue;
                                }
                                // (by area: the points are weighted by the inverse of their density)
                                weighted += Target(ss.value().intr) / ss.value().pdf;
                                weights += 1 / ss.value().pdf;
                            }
                        }
                        meanTarget = weights > 0 ? (Float)(weighted / weights) : 0;
                        meanTargetComputed = true;
                    }
                }
            }
            return meanTarget;
        }

        private volatile bool meanTargetComputed = false;
        private Float meanTarget = 0;

        public Float Temperature(SurfaceInteraction surface)
        {
            return new UniversalTextureEvaluator().Apply(temperature, new TextureEvalContext(surface));
        }

        // The radiance toward w at a hit of the surface
        public SampledSpectrum Radiance(SurfaceInteraction surface, Vector3f w, SampledWavelengths lambda)
        {
            return Radiance(surface, surface.p(), w, lambda);
        }

        // (`seed` is the point that the random numbers of the sample of the reflectance are hashed from: the point as the caller
        // has it, so that the scalar and the polarised value of one evaluation are the same sample)
        private SampledSpectrum Radiance(SurfaceInteraction surface, Point3f seed, Vector3f w, SampledWavelengths lambda)
        {
            SampledSpectrum B = Blackbody(Temperature(surface), lambda);
            if (!B)
            {
                return B;
            }
            if (blackbody)
            {
                return B;
            }
            SampledSpectrum emissivity = Emissivity(surface, seed, w, lambda, false, false, out PLight emitted);
            return B * emissivity;
        }

        // The radiance toward w at a point that was sampled on the shape
        public SampledSpectrum Radiance(Point3f p, Normal3f n, Point2f uv, Vector3f w, SampledWavelengths lambda, bool shapeInRenderSpace)
        {
            return Radiance(SurfaceAt(p, n, uv, w, shapeInRenderSpace), p, w, lambda);
        }

        // The Stokes vector of the radiance toward w at a hit of the surface, in a frame of the direction w. roughPolarisation is
        // the switch of the polarised integrator: the Mueller matrices of the rough scattering are used, not a depolariser.
        public PLight RadiancePolarised(SurfaceInteraction surface, Vector3f w, SampledWavelengths lambda, bool roughPolarisation)
        {
            return RadiancePolarised(surface, surface.p(), w, lambda, roughPolarisation);
        }

        private PLight RadiancePolarised(SurfaceInteraction surface, Point3f seed, Vector3f w, SampledWavelengths lambda,
                                         bool roughPolarisation)
        {
            SampledSpectrum B = Blackbody(Temperature(surface), lambda);
            if (!B)
            {
                return PLight.Zero();
            }
            if (blackbody)
            {
                return new PLight(B);
            }
            Emissivity(surface, seed, w, lambda, true, roughPolarisation, out PLight emitted);
            return emitted * B;
        }

        public PLight RadiancePolarised(Point3f p, Normal3f n, Point2f uv, Vector3f w, SampledWavelengths lambda, bool roughPolarisation,
                                        bool shapeInRenderSpace)
        {
            return RadiancePolarised(SurfaceAt(p, n, uv, w, shapeInRenderSpace), p, w, lambda, roughPolarisation);
        }

        // The mean of the radiance over the shape and over the directions, by the cosine: the power is pi times the area times this
        public SampledSpectrum MeanRadiance(SampledWavelengths lambda)
        {
            const int n = 16;
            RNG rng = new RNG(0x474c4f57UL);
            SampledSpectrum sum = new SampledSpectrum(0.0f);
            for (int i = 0; i < n; ++i)
            {
                Point2f u = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
                Point2f u2 = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
                pstd.optional<ShapeSample> ss = shape.Sample(u);
                if (!ss || ss.value().pdf == 0)
                {
                    continue;
                }
                Interaction intr = ss.value().intr;
                Vector3f w = Frame.FromZ(intr.n).FromLocal(pbrt.SampleCosineHemisphere(u2));
                sum += pbrt.ClampZero(Radiance(intr.p(), intr.n, intr.uv, w, lambda, false));
            }
            return sum / n;
        }

        // One value of the mean radiance, for the bounds of the light sampler
        public Float MeanRadianceBound()
        {
            if (!boundComputed)
            {
                boundValue = MeanRadiance(SampledWavelengths.SampleVisible(0.5f)).MaxComponentValue();
                boundComputed = true;
            }
            return boundValue;
        }

        // The surface at a point of the shape. Sampling a shape gives the point, the normal and (u, v) only; the shape is
        // intersected along the direction for the rest (the tangents that an anisotropic or a bump mapped material needs). A
        // shape that is not in render space (a light that moves) or a ray that does not find the point leaves a frame around
        // the normal, as the emission of an OSL material has.
        private SurfaceInteraction SurfaceAt(Point3f p, Normal3f n, Point2f uv, Vector3f w, bool shapeInRenderSpace)
        {
            if (shapeInRenderSpace)
            {
                Float distance = 1e-3f * (1 + std.max(std.abs(p.x), std.max(std.abs(p.y), std.abs(p.z))));
                Ray ray = new Ray(p + w * distance, -w);
                pstd.optional<ShapeIntersection> hit = shape.Intersect(ray, 2 * distance);
                if (hit && pbrt.DistanceSquared(hit.value().intr.p(), p) < pbrt.Sqr(0.25f * distance))
                {
                    return hit.value().intr;
                }
            }
            Vector3f normal = new Vector3f(n.x, n.y, n.z);
            Vector3f dpdu, dpdv;
            pbrt.CoordinateSystem(normal, &dpdu, &dpdv);
            return new SurfaceInteraction(new Point3fi(p), uv, w, dpdu, dpdv, new Normal3f(0, 0, 0), new Normal3f(0, 0, 0), 0, false);
        }

        // One minus what the material sends toward w of unpolarised surroundings of the radiance one; `emitted` is the same as a
        // Stokes vector (set when polarised)
        private SampledSpectrum Emissivity(SurfaceInteraction surface, Point3f seed, Vector3f w, SampledWavelengths lambda, bool polarised,
                                           bool roughPolarisation, out PLight emitted)
        {
            SampledSpectrum one = new SampledSpectrum(1.0f), zero = new SampledSpectrum(0.0f);
            emitted = PLight.Zero();
            if (material == null)
            {
                return zero;
            }
            if (scratchBuffer == null)
            {
                scratchBuffer = new ScratchBuffer();
            }

            // (the BSDF sets the shading geometry of the interaction: a copy)
            SurfaceInteraction si = surface.Clone();
            si.wo = w;
            si.material = material;
            BSDF bsdf = si.GetBSDF(-w, ref lambda, scratchBuffer);
            if (!bsdf)
            {
                return zero;
            }
            RNG rng = new RNG(pbrt.Hash(seed, w), pbrt.Hash(lambda[0]));
            Float uc = rng.Uniform<Float>();
            Point2f u = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
            return Emissivity(bsdf, si, w, uc, u, opaque, polarised, roughPolarisation, out emitted);
        }

        // The same for a BSDF, with the random numbers of the sample
        public static SampledSpectrum Emissivity(BSDF bsdf, SurfaceInteraction si, Vector3f w, Float uc, Point2f u, bool opaque,
                                                 bool polarised, bool roughPolarisation, out PLight emitted)
        {
            SampledSpectrum one = new SampledSpectrum(1.0f), zero = new SampledSpectrum(0.0f);
            emitted = PLight.Zero();
            BxDF bxdf = bsdf.GetBxDF();
            Vector3f woLocal = bsdf.RenderToLocal(w);
            Vector3f shadingNormal = new Vector3f(si.shading.n.x, si.shading.n.y, si.shading.n.z);
            if (bxdf is DielectricBxDF dielectric && dielectric.IsSpecular())
            {
                // The smooth interface: reflection and transmission are known, there is nothing to sample
                if (!opaque || woLocal.z == 0)
                {
                    return zero;
                }
                pbrt.FrDielectricPolarised(pbrt.CosTheta(woLocal), dielectric.Eta(), out Float Fs, out Float Fp, out Float Ds, out Float Dp);
                if (polarised)
                {
                    Vector3f wiMirror = bsdf.LocalToRender(new Vector3f(-woLocal.x, -woLocal.y, woLocal.z));
                    Attenuation mirror = Attenuation.Fresnel(one, Fs, Fp, Ds, Dp).Scattered(w, wiMirror, shadingNormal, true);
                    emitted = new PLight(one) - mirror * new PLight(one);
                }
                return new SampledSpectrum(1 - 0.5f * (Fs + Fp));
            }

            // The light that a rough interface emits into the medium below it: the radiance of a blackbody in a medium is the
            // square of its index times the one in a vacuum
            SampledSpectrum blackbody = one;
            if (bxdf is DielectricBxDF interfaceBxDF && woLocal.z < 0)
            {
                blackbody = new SampledSpectrum(pbrt.Sqr(interfaceBxDF.Eta()));
            }

            bool pathPolarised = false;
            Attenuation pathMatrix = default;
            // (an opaque body: what is transmitted is absorbed, so the reflection alone is sampled, which also leaves no choice
            // between the two to add noise)
            BxDFReflTransFlags lobes = opaque ? BxDFReflTransFlags.Reflection : BxDFReflTransFlags.All;
            pstd.optional<BSDFSample> bs = polarised && roughPolarisation
                ? bsdf.Sample_f(w, uc, u, TransportMode.Radiance, lobes, out pathPolarised, out pathMatrix)
                : bsdf.Sample_f(w, uc, u, TransportMode.Radiance, lobes);
            if (!bs || (opaque && bs.value().IsTransmission()))
            {
                // Nothing comes from there: all is absorbed
                emitted = new PLight(blackbody);
                return blackbody;
            }

            SampledSpectrum weight = bs.value().f * pbrt.AbsDot(bs.value().wi, si.shading.n) / bs.value().pdf;
            // (the radiance of the surroundings on the other side of an interface is that of a blackbody in that medium: the square
            // of the relative index undoes the scaling of the radiance that crosses)
            Float crossing = bs.value().IsTransmission() ? pbrt.Sqr(bs.value().eta) : 1;
            weight *= crossing;
            if (polarised)
            {
                Attenuation A;
                if (pathPolarised)
                {
                    A = pathMatrix * weight;
                }
                else if (PolarisedPathIntegrator.SpecularAttenuation(bsdf, si, w, bs.value(), TransportMode.Radiance, out A))
                {
                    A *= new SampledSpectrum(crossing);
                }
                else if (!roughPolarisation ||
                         !PolarisedPathIntegrator.RoughAttenuation(bsdf, si, w, bs.value().wi, weight, TransportMode.Radiance, out A))
                {
                    A = Attenuation.Lambert(weight);
                }
                emitted = (new PLight(one) - A * new PLight(one)) * blackbody;
            }
            return (one - weight) * blackbody;
        }
    }
}
