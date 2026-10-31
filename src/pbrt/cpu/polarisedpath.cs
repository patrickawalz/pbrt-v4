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
                                       Measurement measurement)
            : base(camera, sampler, aggregate, lights)
        {
            this.maxDepth = maxDepth;
            this.lightSampler = LightSampler.Create(lightSampleStrategy, lights.ToArray(), new Allocator());
            this.analyser = analyser;
            this.analyserAngle = analyserAngle;
            this.measurement = measurement;
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
        internal static bool SpecularAttenuation(BSDF bsdf, SurfaceInteraction isect, Vector3f wo, BSDFSample bs, TransportMode mode,
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
                    SampledSpectrum Ld = SampleLd(isect, bsdf, ref lambda, sampler);
                    if ((bool)Ld)
                        L += beta * new PLight(Ld);
                }

                Vector3f wo = -ray.d;
                Float u = sampler.Get1D();
                pstd.optional<BSDFSample> bs = bsdf.Sample_f(wo, u, sampler.Get2D());
                if (!bs)
                    break;
                SampledSpectrum scalar = bs.value().f * AbsDot(bs.value().wi, isect.shading.n) / bs.value().pdf;
                if (SpecularAttenuation(bsdf, isect, wo, bs.value(), TransportMode.Radiance, out Attenuation A))
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

            return Measure(L, analyser, analyserAngle, measurement, cameraDirection, cameraUp);
        }

        // What the sensor measures of the light that arrives from the direction toward the camera
        internal static SampledSpectrum Measure(PLight L, bool analyser, Float analyserAngle, Measurement measurement,
                                                Vector3f cameraDirection, Vector3f cameraUp)
        {
            if (!analyser && measurement == Measurement.Intensity)
                return L.GetIntensity();
            Attenuation sensor = analyser ? Attenuation.LinearPolariser(analyserAngle) : Attenuation.Identity();
            sensor = sensor.ForSensor(cameraDirection, cameraUp);
            StokesVector measured = (sensor * L).ToStokesVector();
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

        SampledSpectrum SampleLd(SurfaceInteraction intr, BSDF bsdf, ref SampledWavelengths lambda, Sampler sampler)
        {
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
            SampledSpectrum f = bsdf.f(wo, wi) * AbsDot(wi, intr.shading.n);
            if (!f || !Unoccluded(intr, ls.value().pLight))
                return new SampledSpectrum(0.0f);

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
            return new PolarisedPathIntegrator(maxDepth, camera, sampler, aggregate, lights, lightStrategy, analyser,
                                               analyserAngle, measurement);
        }

        int maxDepth;
        LightSampler lightSampler;
        bool analyser;
        Float analyserAngle;
        Measurement measurement;
        Vector3f cameraUp;
    }
}
