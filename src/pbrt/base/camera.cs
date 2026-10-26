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
        public abstract Float SampleTime(Float u);
        public abstract void InitMetadata(ImageMetadata metadata);
        public abstract CameraTransform GetCameraTransform();
        public abstract void Approximate_dp_dxy(Point3f p, Normal3f n, Float time, int samplesPerPixel,
                                                Vector3f* dpdx, Vector3f* dpdy);
        public abstract SampledSpectrum We(Ray ray, ref SampledWavelengths lambda, Point2f* pRasterOut = null);
        public abstract void PDF_We(Ray ray, Float* pdfPos, Float* pdfDir);
        public abstract pstd.optional<CameraWiSample> SampleWi(Interaction reference, Point2f u, ref SampledWavelengths lambda);
    }

}  // namespace pbrt
