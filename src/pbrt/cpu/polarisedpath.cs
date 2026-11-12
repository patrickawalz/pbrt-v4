// (not in pbrt-v4) cpu/polarisedpath.cs
//
// PolarisedPathIntegrator: PathIntegrator that carries the polarisation of the light along the path with the light system of
// PolarisedUPBP (polarisedlightsystem.cs): the throughput of the camera path is a Mueller matrix (Attenuation) and the
// radiance a Stokes vector (Light) per wavelength. Smooth dielectric and conductor interfaces apply the polarised Fresnel
// equations of Mitsuba 3; every other scattering, the rough ones included, depolarises. The light sources are unpolarised.
// The sensor can have a linear polariser in front of it, to see the polarisation in the image.

using System;
using System.Collections.Generic;
using PLight = pbrt.PolarisedLightSystem.Light;
using Attenuation = pbrt.PolarisedLightSystem.Attenuation;

namespace pbrt
{
    public sealed unsafe class PolarisedPathIntegrator : RayIntegrator
    {
        public enum Measurement { Intensity, S1, S2, S3 }

        // What the polarised integrators measure
        public sealed class Sensor
        {
            public bool analyser;
            public Float analyserAngle;
            public Measurement measurement;
            public Vector3f cameraUp;
        }

        // The Mueller matrix of the scattering in a medium whose phase function has a Rayleigh part, with the scalar phase
        // function divided out: its first element is 1. wo points back along the path, wi on along it. False for the phase
        // functions that scatter all polarisations the same (they depolarise). fromCamera is whether the path is a camera path.
        internal static bool PhaseAttenuation(PhaseFunction phase, Vector3f wo, Vector3f wi, bool fromCamera,
                                              out Attenuation attenuation)
        {
            attenuation = default;
            AtmospherePhase atmosphere = phase as AtmospherePhase;
            if (atmosphere != null)
            {
                // The Mueller matrix of the mixture of the air and the aerosol species, divided by its first element. MieSolver measures Q as
                // I_parallel - I_perpendicular to the scattering plane, the Stokes vectors of this system the other way round: S12 changes sign
                SampledSpectrum s12, s33, s34;
                atmosphere.MuellerRatios(-(double)Dot(wo, wi), out s12, out s33, out s34);
                attenuation = Attenuation.Spherical(-1f * s12, s33, s34);
                attenuation = attenuation.Scattered(wo, wi, fromCamera);
                return true;
            }
            HGPhaseFunction hg = phase as HGPhaseFunction;
            if (hg == null || hg.Rayleigh() == 0)
                return false;
            Float cosTheta = Dot(wo, wi);
            Float f = hg.p(wo, wi);
            if (f <= 0)
                return false;
            Float rayleigh = hg.Rayleigh() * HGPhaseFunction.RayleighPhase(cosTheta);
            attenuation = Attenuation.Rayleigh(new SampledSpectrum(rayleigh / f), new SampledSpectrum(1 - rayleigh / f), cosTheta);
            attenuation = attenuation.Scattered(wo, wi, fromCamera);
            return true;
        }

        public PolarisedPathIntegrator(int maxDepth, Camera camera, Sampler sampler, Primitive aggregate,
                                       List<Light> lights, string lightSampleStrategy, bool analyser, Float analyserAngle,
                                       Measurement measurement, bool roughPolarisation = true)
            : base(camera, sampler, aggregate, lights)
        {
            this.maxDepth = maxDepth;
            this.lightSampler = LightSampler.Create(lightSampleStrategy, lights.ToArray(), new Allocator());
            this.analyser = analyser;
            this.analyserAngle = analyserAngle;
            this.measurement = measurement;
            this.roughPolarisation = roughPolarisation;
            // The x axis of the Stokes frame of the sensor is the up of the image
            this.cameraUp = camera.GetCameraTransform().RenderFromCamera(new Vector3f(0, 1, 0), 0);
        }

        // (not in pbrt-v4) The reflectance (Fs, Fp) of a thin slab for the two polarisations, with the retardances of the first
        // interface: the multiple reflections inside add up incoherently, as ThinDielectricBxDF's R + T^2 R / (1 - R^2)
        static void ThinSlab(Float cosTheta, Float eta, out Float Rs, out Float Rp, out Float Ds, out Float Dp)
        {
            pbrt.FrDielectricPolarised(std.abs(cosTheta), eta, out Float Fs, out Float Fp, out Ds, out Dp);
            Rs = Fs < 1 ? Fs + Sqr(1 - Fs) * Fs / (1 - Sqr(Fs)) : Fs;
            Rp = Fp < 1 ? Fp + Sqr(1 - Fp) * Fp / (1 - Sqr(Fp)) : Fp;
        }

        // (not in pbrt-v4) The Mueller matrix of the mirror reflection of a thin coat: the scalar reflection that was chosen with
        // the probability coat.Reflectance() has the weight one, so the polarised one is relative to that probability
        internal static bool CoatReflection(ThinCoatBxDF coat, Vector3f wo, Vector3f wi, Vector3f normal, TransportMode mode,
                                            out Attenuation A)
        {
            Float cosTheta = Dot(wo, normal);
            ThinSlab(cosTheta, coat.Eta(), out Float Rs, out Float Rp, out Float Ds, out Float Dp);
            Float R = FrDielectric(std.abs(cosTheta), coat.Eta());
            if (R < 1)
                R += Sqr(1 - R) * R / (1 - Sqr(R));
            A = default;
            if (R <= 0)
                return false;
            A = Attenuation.Fresnel(new SampledSpectrum(1 / R), Rs, Rp, Ds, Dp);
            A = A.Scattered(wo, wi, normal, mode == TransportMode.Radiance);
            return true;
        }

        // (not in pbrt-v4) The Mueller matrix of the light that goes through a thin coat in direction d
        internal static bool CoatPassThrough(ThinCoatMaterial coat, Vector3f wo, Vector3f normal, TransportMode mode,
                                             out Attenuation A)
        {
            Float cosTheta = Dot(wo, normal);
            ThinSlab(cosTheta, coat.Eta(), out Float Rs, out Float Rp, out Float Ds, out Float Dp);
            Float T = 1 - coat.Reflectance(cosTheta);
            A = default;
            if (T <= 0)
                return false;
            A = Attenuation.Fresnel(new SampledSpectrum(1 / T), 1 - Rs, 1 - Rp, 0, 0);
            A = A.Scattered(wo, -wo, normal, mode == TransportMode.Radiance);
            return true;
        }

        // The Mueller matrix of a specular interface for a sampled direction, or false when the scattering depolarises
        public static bool SpecularAttenuation(BSDF bsdf, SurfaceInteraction isect, Vector3f wo, BSDFSample bs, TransportMode mode,
                                                 out Attenuation A)
        {
            A = default;
            if (!bs.IsSpecular())
                return false;
            BxDF bxdf = bsdf.GetBxDF();
            Vector3f woLocal = bsdf.RenderToLocal(wo);
            Float cosTheta = CosTheta(woLocal);
            if (bxdf is DielectricBxDF dielectric)
            {
                pbrt.FrDielectricPolarised(cosTheta, dielectric.Eta(), out Float Fs, out Float Fp, out Float Ds, out Float Dp);
                Float R = 0.5f * (Fs + Fp);
                if (bs.IsReflection())
                {
                    if (R <= 0)
                        return false;
                    A = Attenuation.Fresnel(new SampledSpectrum(1 / R), Fs, Fp, Ds, Dp);
                }
                else
                {
                    Float T = 1 - R;
                    if (T <= 0)
                        return false;
                    // The scalar weight of the transmission is 1 / eta^2 in the radiance mode, bs.eta being the relative
                    // index of the side that is entered
                    A = Attenuation.Fresnel(new SampledSpectrum(mode == TransportMode.Radiance ? 1 / T / Sqr(bs.eta) : 1 / T), 1 - Fs, 1 - Fp, 0, 0);
                }
            }
            else if (bxdf is ThinDielectricBxDF thin)
            {
                // (not in pbrt-v4) A slab: the light that is reflected is the sum of what the first interface reflects and of
                // what comes back after 1, 3, 5... reflections inside, and the light that goes through of 0, 2, 4... of them.
                // The orders add without interference, each with its own matrix; all are in the same plane of incidence. With
                // r_s and r_p the signed amplitudes of the outer reflection (those inside are -r, and the two transmissions
                // of a path multiply to 1 - r^2), the reflectances of the two polarisations are R' = 2 R / (1 + R) and
                // T' = (1 - R) / (1 + R) each for its own R, and the term that keeps the polarisation between them is
                //     r_s r_p + (1 - R_s)(1 - R_p) r_s r_p / (1 - R_s R_p)   for the reflection,
                //     (1 - R_s)(1 - R_p) / (1 - R_s R_p)                     for the transmission.
                // It is smaller than sqrt(R'_s R'_p): the slab depolarises a little. The weight of the sample is 1, by the
                // scalar reflectance of pbrt (ThinDielectricBxDF.Sample_f), so the matrix is divided by it.
                pbrt.FrDielectricPolarised(AbsCosTheta(woLocal), thin.Eta(), out Float Fs, out Float Fp, out Float Ds, out Float Dp);
                Float R = 0.5f * (Fs + Fp), T = 1 - R;
                if (R < 1)
                {
                    R += Sqr(T) * R / (1 - Sqr(R));
                    T = 1 - R;
                }
                Float rs = std.sqrt(Fs) * std.cos(Ds), rp = std.sqrt(Fp) * std.cos(Dp);
                Float between = 1 - Fs * Fp;
                Float Is, Ip, C, scalar;
                if (bs.IsReflection())
                {
                    Is = 2 * Fs / (1 + Fs);
                    Ip = 2 * Fp / (1 + Fp);
                    C = rs * rp + (between > 0 ? (1 - Fs) * (1 - Fp) * rs * rp / between : 0);
                    scalar = R;
                }
                else
                {
                    Is = (1 - Fs) / (1 + Fs);
                    Ip = (1 - Fp) / (1 + Fp);
                    C = between > 0 ? (1 - Fs) * (1 - Fp) / between : 0;
                    scalar = T;
                }
                if (scalar <= 0)
                    return false;
                SampledSpectrum a = new SampledSpectrum(0.5f * (Is + Ip) / scalar), b = new SampledSpectrum(0.5f * (Is - Ip) / scalar);
                SampledSpectrum c = new SampledSpectrum(C / scalar), zero = new SampledSpectrum(0.0f);
                A = new Attenuation(new MuellerMatrix(
                    a, b, zero, zero,
                    b, a, zero, zero,
                    zero, zero, c, zero,
                    zero, zero, zero, c));
            }
            else if (bxdf is ThinFilmBxDF film)
            {
                if (!film.Fresnel(cosTheta, out SampledSpectrum Fs, out SampledSpectrum Fp, out SampledSpectrum Ds,
                                  out SampledSpectrum Dp, out SampledSpectrum DsT, out SampledSpectrum DpT))
                    return false;
                // The first element is the ratio of the spectral reflectance to the one of the hero wavelength, which chose
                // between reflection and transmission
                Float R0 = 0.5f * (Fs[0] + Fp[0]);
                if (bs.IsReflection())
                {
                    if (R0 <= 0)
                        return false;
                    A = Attenuation.Fresnel(new SampledSpectrum(1 / R0), Fs, Fp, Ds, Dp);
                }
                else
                {
                    Float T0 = 1 - R0;
                    if (T0 <= 0)
                        return false;
                    A = Attenuation.Fresnel(new SampledSpectrum(mode == TransportMode.Radiance ? 1 / T0 / Sqr(bs.eta) : 1 / T0),
                                            new SampledSpectrum(1.0f) - Fs, new SampledSpectrum(1.0f) - Fp, DsT, DpT);
                }
            }
            else if (bxdf is ThinCoatBxDF coat)
            {
                return CoatReflection(coat, wo, bs.wi, new Vector3f(isect.shading.n.x, isect.shading.n.y, isect.shading.n.z),
                                      mode, out A);
            }
            else if (bxdf is ConductorBxDF conductor)
            {
                SampledSpectrum eta = conductor.Eta(), k = conductor.K();
                SampledSpectrum Fs = new SampledSpectrum(0.0f), Fp = new SampledSpectrum(0.0f);
                SampledSpectrum Ds = new SampledSpectrum(0.0f), Dp = new SampledSpectrum(0.0f);
                for (int i = 0; i < NSpectrumSamples; ++i)
                {
                    pbrt.FrComplexPolarised(AbsCosTheta(woLocal), new pstd.complex(eta[i], k[i]), out Fs[i], out Fp[i],
                                            out Ds[i], out Dp[i]);
                }

                A = Attenuation.Fresnel(new SampledSpectrum(1.0f), Fs, Fp, Ds, Dp);
            }
            else
                return false;

            A = A.Scattered(wo, bs.wi, new Vector3f(isect.shading.n.x, isect.shading.n.y, isect.shading.n.z), mode == TransportMode.Radiance);
            return true;
        }

        // (not in pbrt-v4) The Mueller matrix of a rough (microfacet) conductor or dielectric for the directions wo and wi. The
        // facets are mirrors: the light is polarised by the Fresnel equations at the microfacet normal wm, and the Stokes frames
        // of both directions are those of the plane of that normal (as the rough BSDFs of Mitsuba 3). The first element is
        // weight (the scalar weight of the scattering: f cos / pdf for a sampled direction, one for a connection to a light whose
        // radiance has been multiplied by f cos already). False when the scattering depolarises instead.
        public static bool RoughAttenuation(BSDF bsdf, SurfaceInteraction isect, Vector3f wo, Vector3f wi, SampledSpectrum weight,
                                              TransportMode mode, out Attenuation A)
        {
            A = default;
            BxDF bxdf = bsdf.GetBxDF();
            Vector3f woL = bsdf.RenderToLocal(wo), wiL = bsdf.RenderToLocal(wi);
            Float cosTheta_o = CosTheta(woL), cosTheta_i = CosTheta(wiL);
            if (cosTheta_o == 0 || cosTheta_i == 0)
                return false;
            Vector3f wm;
            SampledSpectrum Fs = new SampledSpectrum(0.0f), Fp = new SampledSpectrum(0.0f);
            SampledSpectrum Ds = new SampledSpectrum(0.0f), Dp = new SampledSpectrum(0.0f);
            SampledSpectrum scale = new SampledSpectrum(0.0f);
            if (bxdf is ConductorBxDF conductor)
            {
                if (conductor.IsSpecular() || !SameHemisphere(woL, wiL))
                    return false;
                wm = woL + wiL;
                if (LengthSquared(wm) == 0)
                    return false;
                wm = FaceForward(Normalize(wm), new Normal3f(0, 0, 1));
                SampledSpectrum eta = conductor.Eta(), k = conductor.K();
                Float cosWm = AbsDot(woL, wm);
                for (int i = 0; i < NSpectrumSamples; ++i)
                {
                    pbrt.FrComplexPolarised(cosWm, new pstd.complex(eta[i], k[i]), out Fs[i], out Fp[i], out Ds[i], out Dp[i]);
                    Float F = 0.5f * (Fs[i] + Fp[i]);
                    scale[i] = F > 0 ? weight[i] / F : 0;
                }
                A = Attenuation.Fresnel(scale, Fs, Fp, Ds, Dp);
            }
            else if (bxdf is DielectricBxDF dielectric)
            {
                if (dielectric.IsSpecular())
                    return false;
                Float eta = dielectric.Eta();
                bool reflect = SameHemisphere(woL, wiL);
                Float etap = reflect ? 1 : (cosTheta_o > 0 ? eta : 1 / eta);
                wm = wiL * etap + woL;
                if (LengthSquared(wm) == 0)
                    return false;
                wm = FaceForward(Normalize(wm), new Normal3f(0, 0, 1));
                if (Dot(wm, wiL) * cosTheta_i < 0 || Dot(wm, woL) * cosTheta_o < 0)
                    return false;
                pbrt.FrDielectricPolarised(Dot(woL, wm), eta, out Float fs, out Float fp, out Float ds, out Float dp);
                Float R = 0.5f * (fs + fp);
                Float norm = reflect ? R : 1 - R;
                if (norm <= 0)
                    return false;
                scale = weight / norm;
                A = reflect ? Attenuation.Fresnel(scale, fs, fp, ds, dp) : Attenuation.Fresnel(scale, 1 - fs, 1 - fp, 0, 0);
            }
            else
            {
                // Every other BxDF builds its own matrix, frames included, with the weight of one
                if (!bxdf.HasMuellerMatrix())
                    return false;
                bool polarised;
                bxdf.f(bsdf, woL, wiL, mode, out polarised, out A);
                if (!polarised)
                    return false;
                A *= weight;
                return true;
            }

            A = A.Scattered(wo, wi, bsdf.LocalToRender(wm), mode == TransportMode.Radiance);
            return true;
        }

        // (not in pbrt-v4) The Mueller matrix of a thin coat that the ray passed through (the surface has no BSDF then)
        internal static bool PassedCoat(SurfaceInteraction isect, Vector3f wo, TransportMode mode, out Attenuation A)
        {
            A = default;
            ThinCoatMaterial coat = isect.material as ThinCoatMaterial;
            if (coat == null)
                return false;
            return CoatPassThrough(coat, wo, new Vector3f(isect.n.x, isect.n.y, isect.n.z), mode, out A);
        }

        public override SampledSpectrum Li(RayDifferential ray, ref SampledWavelengths lambda, Sampler sampler,
                                           ScratchBuffer scratchBuffer, VisibleSurface visibleSurf)
        {
            PLight L = PLight.Zero();
            Attenuation beta = Attenuation.Identity();
            int depth = 0;

            Float p_b = 0, etaScale = 1;
            bool specularBounce = false;
            LightSampleContext prevIntrCtx = default;
            Vector3f cameraDirection = -ray.d;
            Ray cameraRay = new Ray(ray);

            while (true)
            {
                pstd.optional<ShapeIntersection> si = Intersect(new Ray(ray));
                if (!si)
                {
                    foreach (Light light in infiniteLights)
                    {
                        SampledSpectrum Le0 = light.Le(new Ray(ray), lambda);
                        if (depth == 0 || specularBounce)
                            L += beta * new PLight(Le0);
                        else
                        {
                            Float p_l = lightSampler.PMF(prevIntrCtx, light) * light.PDF_Li(prevIntrCtx, ray.d, true);
                            L += beta * new PLight(PowerHeuristic(1, p_b, 1, p_l) * Le0);
                        }
                    }

                    break;
                }

                SampledSpectrum Le = si.value().intr.Le(-ray.d, lambda);
                if ((bool)Le)
                {
                    if (depth == 0 || specularBounce)
                        L += beta * new PLight(Le);
                    else
                    {
                        Light areaLight = si.value().intr.areaLight;
                        Float p_l = lightSampler.PMF(prevIntrCtx, areaLight) * areaLight.PDF_Li(prevIntrCtx, ray.d, true);
                        L += beta * new PLight(PowerHeuristic(1, p_b, 1, p_l) * Le);
                    }
                }

                SurfaceInteraction isect = si.value().intr;
                BSDF bsdf = isect.GetBSDF(ray, ref lambda, camera, scratchBuffer, sampler);
                if (!bsdf)
                {
                    if (PassedCoat(isect, -ray.d, TransportMode.Radiance, out Attenuation Ac))
                        beta *= Ac;
                    specularBounce = true;
                    isect.SkipIntersection(ref ray, si.value().tHit);
                    continue;
                }

                if (depth++ == maxDepth)
                    break;

                if (IsNonSpecular(bsdf.Flags()))
                {
                    // The BxDF gives the matrix with the value it is asked for anyway (one evaluation); the rough conductor and
                    // dielectric of pbrt have theirs built here
                    SampledSpectrum Ld = SampleLd(isect, bsdf, ref lambda, sampler, out Vector3f wiLight, out bool matrixKnown,
                                                  out Attenuation M);
                    if ((bool)Ld)
                    {
                        PLight Lp = new PLight(Ld);
                        if (matrixKnown ||
                            (roughPolarisation && StockRough(bsdf) &&
                             RoughAttenuation(bsdf, isect, -ray.d, wiLight, new SampledSpectrum(1.0f), TransportMode.Radiance, out M)))
                            Lp = M * Lp;
                        L += beta * Lp;
                    }
                }

                Vector3f wo = -ray.d;
                Float u = sampler.Get1D();
                // The matrix of the sampled path itself, from the BxDFs that follow one; the others are asked for the matrix
                // of the pair of directions
                bool pathPolarised = false;
                Attenuation pathMatrix = default;
                Point2f u2 = sampler.Get2D();
                pstd.optional<BSDFSample> bs = roughPolarisation
                    ? bsdf.Sample_f(wo, u, u2, TransportMode.Radiance, BxDFReflTransFlags.All, out pathPolarised, out pathMatrix)
                    : bsdf.Sample_f(wo, u, u2);
                if (!bs)
                    break;
                SampledSpectrum scalar = bs.value().f * AbsDot(bs.value().wi, isect.shading.n) / bs.value().pdf;
                if (pathPolarised)
                    beta *= pathMatrix * scalar;
                else if (SpecularAttenuation(bsdf, isect, wo, bs.value(), TransportMode.Radiance, out Attenuation A) ||
                         (roughPolarisation && RoughAttenuation(bsdf, isect, wo, bs.value().wi, scalar, TransportMode.Radiance, out A)))
                    beta *= A;
                else
                    beta *= Attenuation.Lambert(scalar);
                p_b = bs.value().pdfIsProportional ? bsdf.PDF(wo, bs.value().wi) : bs.value().pdf;
                specularBounce = bs.value().IsSpecular();
                if (bs.value().IsTransmission())
                    etaScale *= Sqr(bs.value().eta);
                prevIntrCtx = new LightSampleContext(si.value().intr);

                ray = isect.SpawnRay(ray, bsdf, bs.value().wi, bs.value().flags, bs.value().eta);

                // The first element of the matrix is the scalar throughput
                SampledSpectrum rrBeta = beta.GetIntensity() * etaScale;
                if (rrBeta.MaxComponentValue() < 1 && depth > 1)
                {
                    Float q = std.max<Float>(0, 1 - rrBeta.MaxComponentValue());
                    if (sampler.Get1D() < q)
                        break;
                    beta *= new SampledSpectrum(1 / (1 - q));
                }
            }

            return MeasureCameraRay(camera, cameraRay, lambda[0], L, analyser, analyserAngle, measurement, cameraDirection, cameraUp);
        }

        // What the sensor measures of the light that arrives from the direction toward the camera
        internal static SampledSpectrum Measure(PLight L, bool analyser, Float analyserAngle, Measurement measurement,
                                                Vector3f cameraDirection, Vector3f cameraUp)
        {
            if (!analyser && measurement == Measurement.Intensity)
                return L.GetIntensity();
            Attenuation sensor = analyser ? Attenuation.LinearPolariser(analyserAngle) : Attenuation.Identity();
            sensor = sensor.ForSensor(cameraDirection, cameraUp);
            return Select((sensor * L).ToStokesVector(), analyser, measurement);
        }

        // (not in pbrt-v4) The same for light that arrives along a camera ray, through the polarising optics of the camera if it has
        // any (see Camera.PolarisedSensor): the light goes through the optics, and the sensor is on the film
        internal static SampledSpectrum MeasureCameraRay(Camera camera, Ray cameraRay, Float wavelength, PLight L, bool analyser,
                                                         Float analyserAngle, Measurement measurement, Vector3f cameraDirection,
                                                         Vector3f cameraUp)
        {
            if (!camera.PolarisedSensor(cameraRay, wavelength, out Attenuation optics, out Vector3f sensorDirection,
                                        out Vector3f sensorUp))
                return Measure(L, analyser, analyserAngle, measurement, cameraDirection, cameraUp);
            Attenuation sensor = analyser ? Attenuation.LinearPolariser(analyserAngle) : Attenuation.Identity();
            sensor = sensor.ForSensor(sensorDirection, sensorUp);
            return Select((sensor * (optics * L)).ToStokesVector(), analyser, measurement);
        }

        static SampledSpectrum Select(StokesVector measured, bool analyser, Measurement measurement)
        {
            switch (measurement)
            {
            case Measurement.S1:
                return measured.s1;
            case Measurement.S2:
                return measured.s2;
            case Measurement.S3:
                return measured.s3;
            default:
                // A linear polariser passes half of the unpolarised light: the factor 2 keeps the brightness
                return analyser ? 2 * measured.s0 : measured.s0;
            }
        }

        // The sensor parameters of the integrators: "analyser" (angle in degrees of a linear polariser in front of the sensor,
        // from the up of the image) and "measure" (intensity, s1, s2 or s3)
        internal static void ParseSensor(ParameterDictionary parameters, FileLoc loc, out bool analyser,
                                         out Float analyserAngle, out Measurement measurement)
        {
            Float angle = parameters.GetOneFloat("analyser", -1000);
            string mode = parameters.GetOneString("measure", "intensity");
            measurement = mode == "s1" ? Measurement.S1 : mode == "s2" ? Measurement.S2 :
                          mode == "s3" ? Measurement.S3 : Measurement.Intensity;
            if (mode != "intensity" && measurement == Measurement.Intensity)
                ErrorExit(loc, StringPrintf("%s: unknown \"measure\", expected intensity, s1, s2 or s3.", mode));
            analyser = angle > -999;
            analyserAngle = Radians(analyser ? angle : 0);
        }

        // Whether the rough matrix of the BSDF is built by RoughAttenuation itself (the BxDFs of pbrt), not by the BxDF
        static bool StockRough(BSDF bsdf)
        {
            BxDF bxdf = bsdf.GetBxDF();
            return bxdf is ConductorBxDF || bxdf is DielectricBxDF;
        }

        SampledSpectrum SampleLd(SurfaceInteraction intr, BSDF bsdf, ref SampledWavelengths lambda, Sampler sampler,
                                 out Vector3f wiLight, out bool matrixKnown, out Attenuation matrix)
        {
            wiLight = default;
            matrixKnown = false;
            matrix = default;
            LightSampleContext ctx = new LightSampleContext(intr);
            BxDFFlags flags = bsdf.Flags();
            if (IsReflective(flags) && !IsTransmissive(flags))
                ctx.pi = intr.OffsetRayOrigin(intr.wo);
            else if (IsTransmissive(flags) && !IsReflective(flags))
                ctx.pi = intr.OffsetRayOrigin(-intr.wo);

            Float u = sampler.Get1D();
            pstd.optional<SampledLight> sampledLight = lightSampler.Sample(ctx, u);
            Point2f uLight = sampler.Get2D();
            if (!sampledLight)
                return new SampledSpectrum(0.0f);

            Light light = sampledLight.value().light;
            pstd.optional<LightLiSample> ls = light.SampleLi(ctx, uLight, lambda, true);
            if (!ls || !ls.value().L || ls.value().pdf == 0)
                return new SampledSpectrum(0.0f);

            Vector3f wo = intr.wo, wi = ls.value().wi;
            SampledSpectrum f = (roughPolarisation && !StockRough(bsdf)
                                     ? bsdf.f(wo, wi, TransportMode.Radiance, out matrixKnown, out matrix)
                                     : bsdf.f(wo, wi)) * AbsDot(wi, intr.shading.n);
            if (!f || !Unoccluded(intr, ls.value().pLight))
                return new SampledSpectrum(0.0f);
            wiLight = wi;

            Float p_l = sampledLight.value().p * ls.value().pdf;
            if (IsDeltaLight(light.Type()))
                return ls.value().L * f / p_l;
            Float p_b = bsdf.PDF(wo, wi);
            return PowerHeuristic(1, p_l, 1, p_b) * ls.value().L * f / p_l;
        }

        public override string ToString()
        {
            return StringPrintf("[ PolarisedPathIntegrator maxDepth: %d analyser: %s angle: %f ]", maxDepth, analyser,
                                analyserAngle);
        }

        public static PolarisedPathIntegrator Create(ParameterDictionary parameters, Camera camera, Sampler sampler,
                                                     Primitive aggregate, List<Light> lights, FileLoc loc)
        {
            int maxDepth = parameters.GetOneInt("maxdepth", 5);
            string lightStrategy = parameters.GetOneString("lightsampler", "bvh");
            ParseSensor(parameters, loc, out bool analyser, out Float analyserAngle, out Measurement measurement);
            // (not in pbrt-v4) "roughpolarisation" false depolarises the rough conductors and dielectrics, as before they were polarised
            return new PolarisedPathIntegrator(maxDepth, camera, sampler, aggregate, lights, lightStrategy, analyser,
                                               analyserAngle, measurement, parameters.GetOneBool("roughpolarisation", true));
        }

        int maxDepth;
        LightSampler lightSampler;
        bool analyser;
        Float analyserAngle;
        Measurement measurement;
        bool roughPolarisation = true;
        Vector3f cameraUp;
    }
}
