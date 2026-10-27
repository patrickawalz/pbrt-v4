// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// base/light.h

using System;

namespace pbrt
{

    // LightType Definition
    public enum LightType { DeltaPosition, DeltaDirection, Area, Infinite }

    // Light Definition
    // (TaggedPointer over PointLight, DistantLight, ProjectionLight, GoniometricLight, SpotLight,
    // DiffuseAreaLight, UniformInfiniteLight, ImageInfiniteLight, PortalImageInfiniteLight)
    public abstract unsafe partial class Light
    {
        // Light Interface
        public abstract SampledSpectrum Phi(SampledWavelengths lambda);
        public abstract LightType Type();
        public abstract pstd.optional<LightLiSample> SampleLi(LightSampleContext ctx, Point2f u, SampledWavelengths lambda,
                                                              bool allowIncompletePDF = false);
        public abstract Float PDF_Li(LightSampleContext ctx, Vector3f wi, bool allowIncompletePDF = false);
        public abstract override string ToString();

        // AreaLights only
        public virtual SampledSpectrum L(Point3f p, Normal3f n, Point2f uv, Vector3f w, SampledWavelengths lambda)
        {
            pbrt.LOG_FATAL("Light::L() called for a light that is not an area light");
            return default;
        }

        // InfiniteLights only
        public virtual SampledSpectrum Le(Ray ray, SampledWavelengths lambda)
        {
            pbrt.LOG_FATAL("Light::Le() called for a light that is not an infinite light");
            return default;
        }

        public abstract void Preprocess(Bounds3f sceneBounds);
        public abstract pstd.optional<LightBounds> Bounds();
        public abstract pstd.optional<LightLeSample> SampleLe(Point2f u1, Point2f u2, ref SampledWavelengths lambda, Float time);
        public abstract void PDF_Le(Ray ray, Float* pdfPos, Float* pdfDir);

        // AreaLights only
        public virtual void PDF_Le(Interaction intr, Vector3f w, Float* pdfPos, Float* pdfDir)
        {
            pbrt.LOG_FATAL("Light::PDF_Le(Interaction) called for a light that is not an area light");
        }
    }

}  // namespace pbrt
