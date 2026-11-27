// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// base/camera.h

using System;

namespace pbrt
{

    // (not in pbrt-v4) Whether the ray that the integrator of this thread is tracing is a ray of stray light whose light from the
    // lights is sampled from the lights (see Camera.HasStrayLight)
    public static class StrayLight
    {
        [ThreadStatic] public static bool skipEmission;
    }

    // Camera Definition (TaggedPointer<PerspectiveCamera, OrthographicCamera, SphericalCamera, RealisticCamera>)
    // The dispatch methods defined in cameras.h/cameras.cpp are the abstract methods here.
    public abstract unsafe partial class Camera
    {
        // Camera Interface
        public abstract override string ToString();
        public abstract pstd.optional<CameraRay> GenerateRay(CameraSample sample, ref SampledWavelengths lambda);
        public abstract pstd.optional<CameraRayDifferential> GenerateRayDifferential(CameraSample sample, ref SampledWavelengths lambda);
        public abstract Film GetFilm();
        // (not in pbrt-v4) The medium the camera rays start in; pbrt-v4 stores it in every Ray
        public abstract Medium GetMedium();
        public abstract Float SampleTime(Float u);
        public abstract void InitMetadata(ImageMetadata metadata);
        public abstract CameraTransform GetCameraTransform();
        public abstract void Approximate_dp_dxy(Point3f p, Normal3f n, Float time, int samplesPerPixel,
                                                Vector3f* dpdx, Vector3f* dpdy);
        public abstract SampledSpectrum We(Ray ray, ref SampledWavelengths lambda, Point2f* pRasterOut = null);
        public abstract void PDF_We(Ray ray, Float* pdfPos, Float* pdfDir);
        public abstract pstd.optional<CameraWiSample> SampleWi(Interaction reference, Point2f u, ref SampledWavelengths lambda);

        // (not in pbrt-v4) The polarisation of the optics between a camera ray and the sensor. A camera ray (in render space, pointing
        // into the scene) carries light that leaves the optics through its origin: optics is the Mueller matrix that the light
        // goes through on its way from there to the film (for light that goes along -ray.d at the origin), sensorDirection the
        // direction that the light has at the film and sensorUp the direction of the up of the image on the film. A camera
        // without such optics (the default) returns false and the sensor is at the origin of the ray, looking along ray.d with the
        // up of the camera. The optics may depend on the wavelength (in nanometres) of the light.
        // (not in pbrt-v4) Whether the image of the camera is convolved with the point spread function of diffraction at its aperture,
        // and the displacement on the film (in pixels) of a sample of that function for the film point pRaster and a wavelength in
        // nanometres: uSelect, uCell and uInCell are uniform random numbers. A camera without diffraction (the default) has none.
        public virtual bool HasDiffraction()
        {
            return false;
        }

        // (not in pbrt-v4) The stray light of the optics of a camera (the ghosts and the veiling glare of RealisticCamera2), for
        // an integrator that starts its paths at the camera. Such a camera gives part of its samples a ray of stray light
        // (StraySample() tells which). The stray light of a light that the lens sees directly is all but impossible to find
        // that way, so the integrator leaves the emission of the lights out of those rays (StrayLight.skipEmission) and
        // samples it from the lights instead: a point of the front element (SampleStrayLens(), with its density over the area),
        // a direction wi from there to a light, and the film point pRaster that the light arrives at with the weight
        // StrayImportance(): the importance of the stray light for a ray that enters the lens there along -wi, times the cosine
        // at the front element. A camera without stray light (the default) has none of this.
        public virtual bool HasStrayLight()
        {
            return false;
        }

        public virtual bool StraySample(CameraSample sample)
        {
            return false;
        }

        public virtual bool SampleStrayLens(Point2f u, Float time, ref SampledWavelengths lambda, out Interaction lens, out Float pdf)
        {
            lens = null;
            pdf = 0;
            return false;
        }

        public virtual SampledSpectrum StrayImportance(Interaction lens, Vector3f wi, Point2f u, ref SampledWavelengths lambda,
                                                       out Point2f pRaster)
        {
            pRaster = default;
            return new SampledSpectrum(0.0f);
        }

        public virtual bool SampleDiffractionOffset(Point2f pRaster, Float wavelength, Float uSelect, Float uCell, Point2f uInCell,
                                                    out Vector2f offsetRaster)
        {
            offsetRaster = default;
            return false;
        }

        public virtual bool PolarisedSensor(Ray ray, Float wavelength, out PolarisedLightSystem.Attenuation optics,
                                            out Vector3f sensorDirection, out Vector3f sensorUp)
        {
            optics = default;
            sensorDirection = default;
            sensorUp = default;
            return false;
        }
    }

}  // namespace pbrt
