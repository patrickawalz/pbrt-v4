// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// base/lightsampler.h

using System;

namespace pbrt
{

    // SampledLight Definition
    public struct SampledLight
    {
        public Light light;
        public Float p;  // = 0

        public override string ToString()
        {
            return Globals.StringPrintf("[ SampledLight light: %s p: %f ]", light != null ? light.ToString() : "(nullptr)", p);
        }
    }

    // LightSampler Definition
    public abstract unsafe partial class LightSampler
    {
        // LightSampler Interface
        public abstract override string ToString();
        public abstract pstd.optional<SampledLight> Sample(LightSampleContext ctx, Float u);
        public abstract Float PMF(LightSampleContext ctx, Light light);
        public abstract pstd.optional<SampledLight> Sample(Float u);
        public abstract Float PMF(Light light);
    }

}  // namespace pbrt
