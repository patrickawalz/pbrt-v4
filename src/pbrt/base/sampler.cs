// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// base/sampler.h

using System;

namespace pbrt
{

    // CameraSample Definition
    public struct CameraSample
    {
        public Point2f pFilm;
        public Point2f pLens;
        public Float time;         // = 0
        public Float filterWeight;  // = 1

        public override string ToString()
        {
            return Globals.StringPrintf("[ CameraSample pFilm: %s pLens: %s time: %f, filterWeight: %f ]",
                                        pFilm, pLens, time, filterWeight);
        }
    }

    // Sampler Definition
    // (TaggedPointer over PMJ02BNSampler, IndependentSampler, StratifiedSampler, HaltonSampler,
    // PaddedSobolSampler, SobolSampler, ZSobolSampler, MLTSampler, DebugMLTSampler)
    public abstract unsafe partial class Sampler
    {
        // Sampler Interface
        public abstract int SamplesPerPixel();
        public abstract void StartPixelSample(Point2i p, int sampleIndex, int dimension = 0);
        public abstract Float Get1D();
        public abstract Point2f Get2D();
        public abstract Point2f GetPixel2D();
        public abstract Sampler Clone(Allocator alloc = null);
        public abstract override string ToString();
    }

}  // namespace pbrt
