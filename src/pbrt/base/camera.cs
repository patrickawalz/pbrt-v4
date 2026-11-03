// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// base/camera.h

using System;

namespace pbrt
{

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
        // up of the camera.
        public virtual bool PolarisedSensor(Ray ray, out PolarisedLightSystem.Attenuation optics, out Vector3f sensorDirection,
                                            out Vector3f sensorUp)
        {
            optics = default;
            sensorDirection = default;
            sensorUp = default;
            return false;
        }
    }

}  // namespace pbrt
