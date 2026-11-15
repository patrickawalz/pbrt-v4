// (not in pbrt-v4) polygrain_access.cs
//
// What ExactPolyGrainBxDF (exactpolygrain.cs) reads of the grains and the bulk of a PolyGrainBxDF.

namespace pbrt
{
    public sealed unsafe partial class PolyGrainBxDF
    {
        public mg.PolyGrain Distribution() { return dist; }
        public BxDF Bulk() { return bulk; }
        public int Samples() { return nSamples; }
        public SampledSpectrum GrainDiffuse(int j) { return Kd[j]; }
        public bool GrainIsSpecular(int j) { return spec[j] != PolyGrainSpecular.None; }
        public Float GrainSpecularProbability(int j, Float cosine, out SampledSpectrum fresnel) { return SpecularProbability(j, cosine, out fresnel); }
    }
}
