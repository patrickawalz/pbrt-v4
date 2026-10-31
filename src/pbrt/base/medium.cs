// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// base/medium.h
//
// PhaseFunction, RayMajorantIterator and Medium are TaggedPointers in C++; here each is an abstract
// class and the types it tags are sealed subclasses (a null TaggedPointer is null). The inline
// dispatch methods defined in media.h become the abstract methods below.

using System;

namespace pbrt
{

    // PhaseFunctionSample Definition
    public struct PhaseFunctionSample
    {
        public Float p;
        public Vector3f wi;
        public Float pdf;
    }

    // PhaseFunction Definition
    public abstract unsafe class PhaseFunction
    {
        // PhaseFunction Interface
        public abstract override string ToString();
        public abstract Float p(Vector3f wo, Vector3f wi);
        public abstract pstd.optional<PhaseFunctionSample> Sample_p(Vector3f wo, Point2f u);
        public abstract Float PDF(Vector3f wo, Vector3f wi);
    }

    // RayMajorantSegment Definition
    public struct RayMajorantSegment
    {
        public Float tMin, tMax;
        public SampledSpectrum sigma_maj;

        public override string ToString()
        {
            return pbrt.StringPrintf("[ RayMajorantSegment tMin: %f tMax: %f sigma_maj: %s ]", tMin, tMax, sigma_maj);
        }
    }

    // RayMajorantIterator Definition
    public abstract unsafe class RayMajorantIterator
    {
        public abstract pstd.optional<RayMajorantSegment> Next();
        public abstract override string ToString();
    }

    // Medium Definition
    public abstract unsafe partial class Medium
    {
        // Medium Interface
        public abstract override string ToString();
        public abstract bool IsEmissive();
        public abstract MediumProperties SamplePoint(Point3f p, SampledWavelengths lambda);

        // Medium Public Methods
        public abstract RayMajorantIterator SampleRay(Ray ray, Float tMax, SampledWavelengths lambda, ScratchBuffer buf);
    }

    // MediumInterface Definition
    // (a struct in C++, always handled through "const MediumInterface *" or by value; as a class,
    // references play the role of the pointers)
    public unsafe class MediumInterface
    {
        // MediumInterface Public Methods
        public override string ToString()
        {
            return pbrt.StringPrintf("[ MediumInterface inside: %s outside: %s ]",
                                        inside != null ? inside.ToString() : "(nullptr)",
                                        outside != null ? outside.ToString() : "(nullptr)");
        }

        public MediumInterface() { }
        public MediumInterface(Medium medium) { inside = medium; outside = medium; }
        public MediumInterface(Medium inside, Medium outside) { this.inside = inside; this.outside = outside; }

        // (not in pbrt-v4) A shape with a priority is a volume boundary (Fong et al., section 3.9): crossing it into the
        // shape enters the inside medium and crossing it out leaves it, whatever medium surrounds the shape, and the medium
        // of highest priority among those a path is in decides the medium of a segment. The outside medium is not used.
        public MediumInterface(Medium inside, Medium outside, int priority)
        {
            this.inside = inside;
            this.outside = outside;
            this.priority = priority;
        }

        public bool IsMediumTransition() { return inside != outside; }

        public bool HasPriority() { return priority != noPriority; }

        // MediumInterface Public Members
        public const int noPriority = int.MinValue;
        public Medium inside, outside;
        public int priority = noPriority;
    }

}  // namespace pbrt
