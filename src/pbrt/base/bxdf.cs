// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// base/bxdf.h
//
// BxDFReflTransFlags, BxDFFlags and TransportMode are enums with overloaded operators in C++ (e.g.
// operator& returns int, operator! on TransportMode); C# enums cannot overload operators, so they are
// small structs with the same named constants. BxDF is a TaggedPointer: abstract class + subclasses.

using System;

namespace pbrt
{
    public static unsafe partial class pbrt
    {
        // BxDFFlags Inline Functions
        public static bool IsReflective(BxDFFlags f) { return (f & BxDFFlags.Reflection) != 0; }
        public static bool IsTransmissive(BxDFFlags f) { return (f & BxDFFlags.Transmission) != 0; }
        public static bool IsDiffuse(BxDFFlags f) { return (f & BxDFFlags.Diffuse) != 0; }
        public static bool IsGlossy(BxDFFlags f) { return (f & BxDFFlags.Glossy) != 0; }
        public static bool IsSpecular(BxDFFlags f) { return (f & BxDFFlags.Specular) != 0; }
        public static bool IsNonSpecular(BxDFFlags f) { return (f & (BxDFFlags.Diffuse | BxDFFlags.Glossy)) != 0; }
    }


    // BxDFReflTransFlags Definition
    public readonly struct BxDFReflTransFlags : IEquatable<BxDFReflTransFlags>
    {
        public readonly int value;
        public BxDFReflTransFlags(int v) { value = v; }

        public static readonly BxDFReflTransFlags Unset = new BxDFReflTransFlags(0);
        public static readonly BxDFReflTransFlags Reflection = new BxDFReflTransFlags(1 << 0);
        public static readonly BxDFReflTransFlags Transmission = new BxDFReflTransFlags(1 << 1);
        public static readonly BxDFReflTransFlags All = new BxDFReflTransFlags((1 << 0) | (1 << 1));

        public static BxDFReflTransFlags operator |(BxDFReflTransFlags a, BxDFReflTransFlags b)
        {
            return new BxDFReflTransFlags(a.value | b.value);
        }
        public static int operator &(BxDFReflTransFlags a, BxDFReflTransFlags b) { return a.value & b.value; }
        public static bool operator ==(BxDFReflTransFlags a, BxDFReflTransFlags b) { return a.value == b.value; }
        public static bool operator !=(BxDFReflTransFlags a, BxDFReflTransFlags b) { return a.value != b.value; }
        public bool Equals(BxDFReflTransFlags o) { return value == o.value; }
        public override bool Equals(object o) { return o is BxDFReflTransFlags f && f.value == value; }
        public override int GetHashCode() { return value; }

        public override string ToString()
        {
            if (this == Unset)
                return "Unset";
            string s = "";
            if ((this & Reflection) != 0)
                s += "Reflection,";
            if ((this & Transmission) != 0)
                s += "Transmission,";
            return s;
        }
    }

    // BxDFFlags Definition
    public readonly struct BxDFFlags : IEquatable<BxDFFlags>
    {
        public readonly int value;
        public BxDFFlags(int v) { value = v; }

        public static readonly BxDFFlags Unset = new BxDFFlags(0);
        public static readonly BxDFFlags Reflection = new BxDFFlags(1 << 0);
        public static readonly BxDFFlags Transmission = new BxDFFlags(1 << 1);
        public static readonly BxDFFlags Diffuse = new BxDFFlags(1 << 2);
        public static readonly BxDFFlags Glossy = new BxDFFlags(1 << 3);
        public static readonly BxDFFlags Specular = new BxDFFlags(1 << 4);
        // Composite _BxDFFlags_ definitions
        public static readonly BxDFFlags DiffuseReflection = new BxDFFlags((1 << 2) | (1 << 0));
        public static readonly BxDFFlags DiffuseTransmission = new BxDFFlags((1 << 2) | (1 << 1));
        public static readonly BxDFFlags GlossyReflection = new BxDFFlags((1 << 3) | (1 << 0));
        public static readonly BxDFFlags GlossyTransmission = new BxDFFlags((1 << 3) | (1 << 1));
        public static readonly BxDFFlags SpecularReflection = new BxDFFlags((1 << 4) | (1 << 0));
        public static readonly BxDFFlags SpecularTransmission = new BxDFFlags((1 << 4) | (1 << 1));
        public static readonly BxDFFlags All = new BxDFFlags((1 << 2) | (1 << 3) | (1 << 4) | (1 << 0) | (1 << 1));

        public static BxDFFlags operator |(BxDFFlags a, BxDFFlags b) { return new BxDFFlags(a.value | b.value); }
        public static int operator &(BxDFFlags a, BxDFFlags b) { return a.value & b.value; }
        public static int operator &(BxDFFlags a, BxDFReflTransFlags b) { return a.value & b.value; }
        public static bool operator ==(BxDFFlags a, BxDFFlags b) { return a.value == b.value; }
        public static bool operator !=(BxDFFlags a, BxDFFlags b) { return a.value != b.value; }
        public bool Equals(BxDFFlags o) { return value == o.value; }
        public override bool Equals(object o) { return o is BxDFFlags f && f.value == value; }
        public override int GetHashCode() { return value; }

        public override string ToString()
        {
            if (this == Unset)
                return "Unset";
            string s = "";
            if ((this & Reflection) != 0)
                s += "Reflection,";
            if ((this & Transmission) != 0)
                s += "Transmission,";
            if ((this & Diffuse) != 0)
                s += "Diffuse,";
            if ((this & Glossy) != 0)
                s += "Glossy,";
            if ((this & Specular) != 0)
                s += "Specular,";
            return s;
        }
    }

    // TransportMode Definition
    public readonly struct TransportMode : IEquatable<TransportMode>
    {
        public readonly int value;
        private TransportMode(int v) { value = v; }

        public static readonly TransportMode Radiance = new TransportMode(0);
        public static readonly TransportMode Importance = new TransportMode(1);

        public static TransportMode operator !(TransportMode mode)
        {
            return (mode == Radiance) ? Importance : Radiance;
        }
        public static bool operator ==(TransportMode a, TransportMode b) { return a.value == b.value; }
        public static bool operator !=(TransportMode a, TransportMode b) { return a.value != b.value; }
        public bool Equals(TransportMode o) { return value == o.value; }
        public override bool Equals(object o) { return o is TransportMode m && m.value == value; }
        public override int GetHashCode() { return value; }

        public override string ToString()
        {
            return this == Radiance ? "Radiance" : "Importance";
        }
    }

    // BSDFSample Definition
    public struct BSDFSample
    {
        // BSDFSample Public Methods
        public BSDFSample()
        {
            f = default;
            wi = default;
            pdf = 0;
            flags = BxDFFlags.Unset;
            eta = 1;
            pdfIsProportional = false;
        }
        public BSDFSample(SampledSpectrum f, Vector3f wi, Float pdf, BxDFFlags flags, Float eta = 1,
                          bool pdfIsProportional = false)
        {
            this.f = f;
            this.wi = wi;
            this.pdf = pdf;
            this.flags = flags;
            this.eta = eta;
            this.pdfIsProportional = pdfIsProportional;
        }

        public bool IsReflection() { return pbrt.IsReflective(flags); }
        public bool IsTransmission() { return pbrt.IsTransmissive(flags); }
        public bool IsDiffuse() { return pbrt.IsDiffuse(flags); }
        public bool IsGlossy() { return pbrt.IsGlossy(flags); }
        public bool IsSpecular() { return pbrt.IsSpecular(flags); }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ BSDFSample f: %s wi: %s pdf: %f flags: %s eta: %f pdfIsProportional: %s ]",
                                        f, wi, pdf, flags, eta, pdfIsProportional);
        }

        public SampledSpectrum f;
        public Vector3f wi;
        public Float pdf = 0;
        public BxDFFlags flags;
        public Float eta = 1;
        public bool pdfIsProportional;
    }

    // BxDF Definition
    public abstract unsafe partial class BxDF
    {
        // BxDF Interface
        public abstract BxDFFlags Flags();
        public abstract override string ToString();
        public abstract SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode);
        public abstract pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode,
                                                           BxDFReflTransFlags sampleFlags);
        public pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode)
        {
            return Sample_f(wo, uc, u, mode, BxDFReflTransFlags.All);
        }
        public pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u)
        {
            return Sample_f(wo, uc, u, TransportMode.Radiance, BxDFReflTransFlags.All);
        }
        public abstract Float PDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags);
        public Float PDF(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            return PDF(wo, wi, mode, BxDFReflTransFlags.All);
        }
        public abstract void Regularize();

        // rho() is defined in bxdfs.cs (BxDF::rho in bxdfs.cpp)
    }

}  // namespace pbrt
