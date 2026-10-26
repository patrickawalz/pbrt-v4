// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// base/material.h
//
// The member templates on TextureEvaluator are generic methods constrained by ITextureEvaluator
// (declared with TextureEvalContext in textures.cs).

using System;

namespace pbrt
{

    // Material Definition
    // (TaggedPointer over CoatedDiffuseMaterial, CoatedConductorMaterial, ConductorMaterial,
    // DielectricMaterial, DiffuseMaterial, DiffuseTransmissionMaterial, HairMaterial,
    // MeasuredMaterial, SubsurfaceMaterial, ThinDielectricMaterial, MixMaterial)
    public abstract unsafe partial class Material
    {
        // Material Interface
        public abstract override string ToString();
        public abstract BSDF GetBSDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                       ref SampledWavelengths lambda, ScratchBuffer buf)
            where TextureEvaluator : ITextureEvaluator;
        public abstract BSSRDF GetBSSRDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                           ref SampledWavelengths lambda, ScratchBuffer buf)
            where TextureEvaluator : ITextureEvaluator;
        public abstract bool CanEvaluateTextures<TextureEvaluator>(TextureEvaluator texEval)
            where TextureEvaluator : ITextureEvaluator;
        public abstract Image GetNormalMap();
        public abstract FloatTexture GetDisplacement();
        public abstract bool HasSubsurfaceScattering();
    }

}  // namespace pbrt
