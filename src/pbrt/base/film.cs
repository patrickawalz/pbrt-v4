// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// base/film.h

using System;

namespace pbrt
{

    // Film Definition (TaggedPointer<RGBFilm, GBufferFilm, SpectralFilm>)
    public abstract unsafe partial class Film
    {
        // Film Interface
        public abstract void AddSample(Point2i pFilm, SampledSpectrum L, SampledWavelengths lambda,
                                       VisibleSurface visibleSurface, Float weight);
        public abstract Bounds2f SampleBounds();
        public abstract bool UsesVisibleSurface();
        public abstract void AddSplat(Point2f p, SampledSpectrum v, SampledWavelengths lambda);
        public abstract SampledWavelengths SampleWavelengths(Float u);
        public abstract Point2i FullResolution();
        public abstract Bounds2i PixelBounds();
        public abstract Float Diagonal();
        public abstract void WriteImage(ImageMetadata metadata, Float splatScale = 1);
        public abstract RGB ToOutputRGB(SampledSpectrum L, SampledWavelengths lambda);
        public abstract Image GetImage(ImageMetadata metadata, Float splatScale = 1);
        public abstract RGB GetPixelRGB(Point2i p, Float splatScale = 1);
        public abstract Filter GetFilter();
        public abstract PixelSensor GetPixelSensor();
        public abstract string GetFilename();
        public abstract override string ToString();
        public abstract void ResetPixel(Point2i p);
    }

}  // namespace pbrt
