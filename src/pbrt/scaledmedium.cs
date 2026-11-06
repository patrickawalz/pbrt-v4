// (not in pbrt-v4) scaledmedium.cs
//
// The volume that a material creates for the point where a path enters it (Fong et al., "Production Volume Rendering",
// listing 1, Material::CreateVolume(ShadingContext)): the medium that the material names, with the coefficients scaled by the
// value of the material's "volumescale" texture at the point of entry.

using System;

namespace pbrt
{

    // ScaledMajorantIterator Definition
    public sealed unsafe class ScaledMajorantIterator : RayMajorantIterator
    {
        // ScaledMajorantIterator Public Methods
        public ScaledMajorantIterator(RayMajorantIterator inner, Float scale)
        {
            this.inner = inner;
            this.scale = scale;
        }

        public override pstd.optional<RayMajorantSegment> Next()
        {
            pstd.optional<RayMajorantSegment> seg = inner.Next();
            if (!seg)
                return default;
            RayMajorantSegment scaled = seg.value();
            scaled.sigma_maj = scaled.sigma_maj * scale;
            return scaled;
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ ScaledMajorantIterator scale: %f ]", scale);
        }

        // ScaledMajorantIterator Private Members
        private RayMajorantIterator inner;
        private Float scale;
    }

    // ScaledMedium Definition
    public sealed unsafe class ScaledMedium : Medium, IMajorantMedium<RayMajorantIterator>
    {
        // ScaledMedium Public Methods
        public ScaledMedium(Medium medium, Float scale)
        {
            this.medium = medium;
            this.scale = scale;
            volumeIntegrator = medium.volumeIntegrator;
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ ScaledMedium scale: %f medium: %s ]", scale, medium);
        }

        public override bool IsEmissive() { return medium.IsEmissive(); }

        public Medium Inner() { return medium; }

        public override MediumProperties SamplePoint(Point3f p, SampledWavelengths lambda)
        {
            MediumProperties mp = medium.SamplePoint(p, lambda);
            mp.sigma_a = mp.sigma_a * scale;
            mp.sigma_s = mp.sigma_s * scale;
            return mp;
        }

        public override MediumProperties SamplePoint(Point3f p, SampledWavelengths lambda, Float time)
        {
            MediumProperties mp = medium.SamplePoint(p, lambda, time);
            mp.sigma_a = mp.sigma_a * scale;
            mp.sigma_s = mp.sigma_s * scale;
            return mp;
        }

        public RayMajorantIterator SampleRay(Ray ray, Float tMax, SampledWavelengths lambda)
        {
            return SampleRay(ray, tMax, lambda, new ScratchBuffer());
        }

        public override RayMajorantIterator SampleRay(Ray ray, Float tMax, SampledWavelengths lambda, ScratchBuffer buf)
        {
            return buf.Alloc(new ScaledMajorantIterator(medium.SampleRay(ray, tMax, lambda, buf), scale));
        }

        public override SampledSpectrum ControlExtinction(SampledWavelengths lambda)
        {
            return medium.ControlExtinction(lambda) * scale;
        }

        // ScaledMedium Private Members
        private Medium medium;
        private Float scale;
    }

}  // namespace pbrt
