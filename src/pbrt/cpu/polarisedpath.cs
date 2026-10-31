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

        // The Mueller matrix of a specular interface for a sampled direction, or false when the scattering depolarises
        bool SpecularAttenuation(BSDF bsdf, SurfaceInteraction isect, Vector3f wo, BSDFSample bs, out Attenuation A)
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
                    A = Attenuation.Fresnel(new SampledSpectrum(1 / T / Sqr(bs.eta)), 1 - Fs, 1 - Fp, 0, 0);
                }
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

            A = A.Scattered(wo, bs.wi, new Vector3f(isect.shading.n.x, isect.shading.n.y, isect.shading.n.z), true);
            return true;
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
                if (SpecularAttenuation(bsdf, isect, wo, bs.value(), out Attenuation A))
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

            // What the sensor measures
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
            // The angle in degrees of the linear polariser in front of the sensor, from the up of the image; none by default
            Float angle = parameters.GetOneFloat("analyser", -1000);
            string mode = parameters.GetOneString("measure", "intensity");
            Measurement measurement = mode == "s1" ? Measurement.S1 : mode == "s2" ? Measurement.S2 :
                                      mode == "s3" ? Measurement.S3 : Measurement.Intensity;
            if (mode != "intensity" && measurement == Measurement.Intensity)
                ErrorExit(loc, StringPrintf("%s: unknown \"measure\", expected intensity, s1, s2 or s3.", mode));
            bool analyser = angle > -999;
            return new PolarisedPathIntegrator(maxDepth, camera, sampler, aggregate, lights, lightStrategy, analyser,
                                               Radians(analyser ? angle : 0), measurement);
        }

        int maxDepth;
        LightSampler lightSampler;
        bool analyser;
        Float analyserAngle;
        Measurement measurement;
        Vector3f cameraUp;
    }
}
