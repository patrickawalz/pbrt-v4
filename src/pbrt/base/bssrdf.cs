// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// base/bssrdf.h

using System;

namespace pbrt
{

    // BSSRDF is a TaggedPointer<TabulatedBSSRDF> in C++
    public abstract unsafe class BSSRDF
    {
        // BSSRDF Interface
        public abstract pstd.optional<BSSRDFProbeSegment> SampleSp(Float u1, Point2f u2);
        public abstract BSSRDFSample ProbeIntersectionToSample(SubsurfaceInteraction si, ScratchBuffer scratchBuffer);
    }

}  // namespace pbrt
