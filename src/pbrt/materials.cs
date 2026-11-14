// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// materials.h, materials.cpp
//
// The C++ materials are TaggedPointer alternatives with a `using BxDF = ...` / `using BSSRDF = ...`
// type alias each; Material::GetBSDF()/GetBSSRDF() dispatch on those aliases. Here each concrete
// material is a subclass of Material (base/material.cs) that overrides GetBSDF()/GetBSSRDF()
// itself, doing exactly what the dispatching lambdas did for that material.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace pbrt
{
    public static unsafe partial class pbrt
    {
        // Normal Mapping Function Definitions
        public static void NormalMap(Image normalMap, NormalBumpEvalContext ctx, Vector3f* dpdu, Vector3f* dpdv)
        {
            // Get normalized normal vector from normal map
            WrapMode2D wrap = new WrapMode2D(WrapMode.Repeat);
            Point2f uv = new Point2f(ctx.uv[0], 1 - ctx.uv[1]);
            Vector3f ns = new Vector3f(2 * normalMap.BilerpChannel(uv, 0, wrap) - 1,
                                       2 * normalMap.BilerpChannel(uv, 1, wrap) - 1,
                                       2 * normalMap.BilerpChannel(uv, 2, wrap) - 1);
            ns = Normalize(ns);

            // Transform tangent-space normal to rendering space
            Frame frame = Frame.FromXZ(Normalize(ctx.shading.dpdu), (Vector3f)ctx.shading.n);
            ns = frame.FromLocal(ns);

            // Find $\dpdu$ and $\dpdv$ that give shading normal
            Float ulen = Length(ctx.shading.dpdu), vlen = Length(ctx.shading.dpdv);
            *dpdu = Normalize(GramSchmidt(ctx.shading.dpdu, ns)) * ulen;
            *dpdv = Normalize(Cross(ns, *dpdu)) * vlen;
        }

        // Bump Mapping Function Definitions
        public static void BumpMap<TextureEvaluator>(TextureEvaluator texEval, FloatTexture displacement,
                                                     NormalBumpEvalContext ctx, Vector3f* dpdu, Vector3f* dpdv)
            where TextureEvaluator : ITextureEvaluator
        {
            DCHECK(texEval.CanEvaluate(new FloatTexture[] { displacement }, new SpectrumTexture[0]));
            // (not as in pbrt-v4) The displacement and its change along the two tangents of the shading geometry, from
            // the texture: in closed form where the texture has one, else by the finite difference that was here (the
            // context moved by half the size of the pixel along dpdu, and along dpdv), which Option "string
            // texturederivatives" "finite" brings back for every texture
            TextureDirections directions = new TextureDirections(ctx.shading.dpdu, ctx.shading.dpdv);
            Float dDisplaceDu, dDisplaceDv;
            Float displace = displacement.Evaluate(ctx, directions, &dDisplaceDu, &dDisplaceDv);

            // Compute bump-mapped differential geometry
            *dpdu = ctx.shading.dpdu + dDisplaceDu * (Vector3f)ctx.shading.n + displace * (Vector3f)ctx.shading.dndu;
            *dpdv = ctx.shading.dpdv + dDisplaceDv * (Vector3f)ctx.shading.n + displace * (Vector3f)ctx.shading.dndv;
        }

        // STAT_COUNTER("Scene/Materials", nMaterialsCreated);
        [ThreadStatic] internal static long nMaterialsCreated;
        private static StatRegisterer STATS_REGnMaterialsCreated = new StatRegisterer((StatsAccumulator accum) =>
        {
            accum.ReportCounter("Scene/Materials", nMaterialsCreated);
            nMaterialsCreated = 0;
        });

    }


    // MaterialEvalContext Definition
    // (C++: struct MaterialEvalContext : public TextureEvalContext. C# structs cannot inherit,
    // so the TextureEvalContext members are repeated and there is an implicit conversion.)
    public struct MaterialEvalContext
    {
        // MaterialEvalContext Public Methods
        public MaterialEvalContext(SurfaceInteraction si)
        {
            TextureEvalContext t = new TextureEvalContext(si);
            p = t.p; dpdx = t.dpdx; dpdy = t.dpdy; n = t.n; uv = t.uv;
            dudx = t.dudx; dudy = t.dudy; dvdx = t.dvdx; dvdy = t.dvdy; faceIndex = t.faceIndex; objectId = t.objectId;
            wo = si.wo;
            ns = si.shading.n;
            dpdus = si.shading.dpdu;
            coatN = si.coatN;
            this.si = si;
        }

        public static implicit operator MaterialEvalContext(SurfaceInteraction si) { return new MaterialEvalContext(si); }

        public static implicit operator TextureEvalContext(MaterialEvalContext c)
        {
            TextureEvalContext t = new TextureEvalContext(c.p, c.dpdx, c.dpdy, c.n, c.uv, c.dudx, c.dudy, c.dvdx, c.dvdy, c.faceIndex, c.objectId);
            t.si = c.si;
            return t;
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ MaterialEvalContext %s wo: %s ns: %s dpdus: %s ]",
                                        ((TextureEvalContext)this).ToString(), wo, ns, dpdus);
        }

        // TextureEvalContext members
        public Point3f p;
        public Vector3f dpdx, dpdy;
        public Normal3f n;
        public Point2f uv;
        public Float dudx, dudy, dvdx, dvdy;
        public int faceIndex;
        public int objectId;
        // MaterialEvalContext members
        public Vector3f wo;
        public Normal3f ns;
        public Vector3f dpdus;
        // (not in pbrt-v4) the normal of the coat (SurfaceInteraction.coatN); zero if there is none
        public Vector3f coatN;
        // (not in pbrt-v4) the interaction the context was made from (null if made from other data); OSL shaders read the whole interaction
        public SurfaceInteraction si;
    }

    // NormalBumpEvalContext Definition
    public struct NormalBumpEvalContext
    {
        // NormalBumpEvalContext Public Methods
        public NormalBumpEvalContext(SurfaceInteraction si)
        {
            p = si.p();
            uv = si.uv;
            n = si.n;
            dudx = si.dudx;
            dudy = si.dudy;
            dvdx = si.dvdx;
            dvdy = si.dvdy;
            dpdx = si.dpdx;
            dpdy = si.dpdy;
            faceIndex = si.faceIndex;
            objectId = si.objectId;
            shading = new ShadingGeometry();
            shading.n = si.shading.n;
            shading.dpdu = si.shading.dpdu;
            shading.dpdv = si.shading.dpdv;
            shading.dndu = si.shading.dndu;
            shading.dndv = si.shading.dndv;
            gdpdu = si.dpdu;
            gdpdv = si.dpdv;
            this.si = si;
        }

        public static implicit operator TextureEvalContext(NormalBumpEvalContext c)
        {
            TextureEvalContext t = new TextureEvalContext(c.p, c.dpdx, c.dpdy, c.n, c.uv, c.dudx, c.dudy, c.dvdx, c.dvdy, c.faceIndex, c.objectId);
            // (not in pbrt-v4) The intersection: a displacement on another channel of texture coordinates reads it there
            t.si = c.si;
            return t;
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ NormalBumpEvalContext p: %s uv: %s shading.n: %s shading.dpdu: %s " +
                                        "shading.dpdv: %s shading.dndu: %s shading.dndv: %s dudx: %f " +
                                        "dudy: %f dvdx: %f dvdy: %f dpdx: %s dpdy: %s faceIndex: %d ]",
                                        p, uv, shading.n, shading.dpdu, shading.dpdv, shading.dndu, shading.dndv, dudx,
                                        dudy, dvdx, dvdy, dpdx, dpdy, faceIndex);
        }

        // NormalBumpEvalContext Public Members
        public struct ShadingGeometry
        {
            public Normal3f n;
            public Vector3f dpdu, dpdv;
            public Normal3f dndu, dndv;
        }
        public Point3f p;
        public Point2f uv;
        public Normal3f n;
        public ShadingGeometry shading;
        public Float dudx, dudy, dvdx, dvdy;
        public Vector3f dpdx, dpdy;
        public int faceIndex;
        public int objectId;
        // (not in pbrt-v4) The derivatives of the true geometry (a triangle with shading normals replaces shading.dpdv by
        // Cross(ns, dpdu)), and the intersection, for the other channels of texture coordinates
        public Vector3f gdpdu, gdpdv;
        public SurfaceInteraction si;
    }

    // DielectricMaterial Definition
    public sealed unsafe class DielectricMaterial : Material
    {
        // DielectricMaterial Public Methods
        public DielectricMaterial(FloatTexture uRoughness, FloatTexture vRoughness, Spectrum eta, FloatTexture displacement,
                                  Image normalMap, bool remapRoughness,
                                  MicrofacetDistributionType distribution = MicrofacetDistributionType.TrowbridgeReitz, Float gamma = 2)
        {
            this.distribution = distribution;
            this.gamma = gamma;
            this.normalMap = normalMap;
            this.displacement = displacement;
            this.uRoughness = uRoughness;
            this.vRoughness = vRoughness;
            this.eta = eta;
            this.remapRoughness = remapRoughness;
        }

        public static string Name() { return "DielectricMaterial"; }

        public override bool CanEvaluateTextures<TextureEvaluator>(TextureEvaluator texEval)
        {
            return texEval.CanEvaluate(new FloatTexture[] { uRoughness, vRoughness }, new SpectrumTexture[0]);
        }

        public override FloatTexture GetDisplacement() { return displacement; }
        public override Image GetNormalMap() { return normalMap; }
        public override bool HasSubsurfaceScattering() { return false; }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ DielectricMaterial displacement: %s normalMap: %s uRoughness: %s " +
                                        "vRoughness: %s eta: %s remapRoughness: %s ]",
                                        displacement, (normalMap != null ? normalMap.ToString() : "(nullptr)"), uRoughness, vRoughness, eta, remapRoughness);
        }

        public override BSSRDF GetBSSRDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                           ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            return null;
        }

        public static DielectricMaterial Create(TextureParameterDictionary parameters, Image normalMap, FileLoc loc, Allocator alloc)
        {
            // (not as in pbrt-v4) The index, with the dispersion formulas of the C++ fork ("sellmeier", "cauchy", "cauchyb")
            Spectrum eta = ParametricSpectra.Index(parameters, "eta", alloc);
            // (not in pbrt-v4) "texture eta": an index that varies over the surface (a spectrum texture)
            SpectrumTexture etaTexture = ParametricSpectra.IndexTexture(parameters, "eta", eta, alloc);
            if (eta == null)
                eta = new ConstantSpectrum(1.5f);
            // (not in pbrt-v4) "float abbe": the index given is the one at the sodium D line, and the dispersion follows from the
            // Abbe number (AbbeDispersionSpectrum). Without it a single index has no dispersion, as before.
            Float abbe = parameters.GetOneFloat("abbe", 0f);
            if (abbe != 0)
            {
                if (!(eta is ConstantSpectrum))
                    ErrorExit(loc, "\"abbe\" needs a single index of refraction (\"float eta\"), not a spectrum.");
                eta = new AbbeDispersionSpectrum(eta.Evaluate(AbbeDispersionSpectrum.LambdaD), abbe);
            }

            FloatTexture uRoughness = parameters.GetFloatTextureOrNull("uroughness", alloc);
            FloatTexture vRoughness = parameters.GetFloatTextureOrNull("vroughness", alloc);
            if (uRoughness == null)
                uRoughness = parameters.GetFloatTexture("roughness", 0f, alloc);
            if (vRoughness == null)
                vRoughness = parameters.GetFloatTexture("roughness", 0f, alloc);

            FloatTexture displacement = parameters.GetFloatTextureOrNull("displacement", alloc);
            bool remapRoughness = parameters.GetOneBool("remaproughness", true);
            // (not in pbrt-v4) "string distribution": "ggx" (the default), "beckmann" or "studentt", with "float gamma" for the last
            MicrofacetDistributionType distribution = MicrofacetDistribution.ParseType(parameters.GetOneString("distribution", "ggx"), loc);
            Float gamma = parameters.GetOneFloat("gamma", 2f);
            DielectricMaterial dielectric = new DielectricMaterial(uRoughness, vRoughness, eta, displacement, normalMap, remapRoughness, distribution, gamma);
            dielectric.retroreflectivity = Clamp(parameters.GetOneFloat("retroreflectivity", 0f), 0, 1);
            dielectric.etaTexture = etaTexture;
            return dielectric;
        }

        public override BSDF GetBSDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                       ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            DielectricBxDF bxdf = GetBxDF(texEval, ctx, ref lambda);
            if (retroreflectivity > 0)
            {
                return new BSDF(ctx.n, ctx.ns, ctx.dpdus, buf.Alloc(new RetroreflectiveBxDF(bxdf, retroreflectivity)));
            }
            return new BSDF(ctx.n, ctx.ns, ctx.dpdus, buf.Alloc(bxdf));
        }

        // (not in pbrt-v4) "float retroreflectivity": the share of the reflection that is evaluated at the mirrored view
        // direction (RetroreflectiveBxDF); the transmission is unchanged. 0 = off
        public Float retroreflectivity = 0;

        public DielectricBxDF GetBxDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                        ref SampledWavelengths lambda)
            where TextureEvaluator : ITextureEvaluator
        {
            // Compute index of refraction for dielectric material
            // (not as in pbrt-v4) with "texture eta", the index of the texture at the point
            Float sampledEta = ParametricSpectra.SampleIndex(eta, etaTexture, texEval, ctx, ref lambda);

            // Create microfacet distribution for dielectric material
            Float urough = texEval.Apply(uRoughness, ctx), vrough = texEval.Apply(vRoughness, ctx);
            if (remapRoughness)
            {
                urough = TrowbridgeReitzDistribution.RoughnessToAlpha(urough);
                vrough = TrowbridgeReitzDistribution.RoughnessToAlpha(vrough);
            }
            MicrofacetDistribution distrib = MicrofacetDistribution.Create(distribution, urough, vrough, gamma);

            // Return BSDF for dielectric material
            return new DielectricBxDF(sampledEta, distrib);
        }

        // (not in pbrt-v4) "texture eta": null unless the index is a texture
        public SpectrumTexture etaTexture = null;

        // DielectricMaterial Private Members
        private Image normalMap;
        private FloatTexture displacement;
        private FloatTexture uRoughness, vRoughness;
        private bool remapRoughness;
        private Spectrum eta;
        private MicrofacetDistributionType distribution;
        private Float gamma;
    }

    // (not in pbrt-v4) ThinCoatMaterial Definition: a thin clear dielectric coat over whatever is behind it (a medium, usually).
    // Like ThinDielectricMaterial, light goes straight through it or is mirrored, with the reflectance of a thin slab. Unlike
    // it, the choice is made once for the surface point and the direction, by a hash: the surface acts as a null interface for
    // the rays that pass (see SurfaceInteraction.GetBSDF()) and the shadow rays that pass, so light sampling still works through it.
    // The probabilities of a path and of a shadow ray going through are the same, so the multiple importance sampling weights,
    // which do not contain them, stay consistent, and the estimate is unbiased.
    public sealed unsafe class ThinCoatMaterial : Material
    {
        // Set when a coat is created, so that scenes without one keep the plain shadow ray test
        public static bool anyInScene;

        public ThinCoatMaterial(Float eta, FloatTexture displacement, Image normalMap)
        {
            this.eta = eta; this.displacement = displacement; this.normalMap = normalMap;
            anyInScene = true;
        }

        // The reflectance of a thin slab, as ThinDielectricBxDF computes it
        public Float Reflectance(Float cosTheta)
        {
            Float R = FrDielectric(std.abs(cosTheta), eta), T = 1 - R;
            if (R < 1)
            {
                R += Sqr(T) * R / (1 - Sqr(R));
            }
            return R;
        }

        public Float Eta() { return eta; }

        // Whether the ray or shadow ray that arrives with direction w goes through the coat at this point
        public bool PassesThrough(SurfaceInteraction intr, Vector3f w)
        {
            w = Normalize(w);
            RNG rng = new RNG(Hash(intr.p()), Hash(w));
            return rng.Uniform<Float>() >= Reflectance(Dot(w, intr.n));
        }

        // Whether a shadow ray with direction d is stopped by the surface that it hit
        public static bool Blocks(SurfaceInteraction intr, Vector3f d)
        {
            Material material = intr.material;
            if (material == null)
                return false;
            ThinCoatMaterial coat = material as ThinCoatMaterial;
            return coat == null || !coat.PassesThrough(intr, d);
        }

        public override bool CanEvaluateTextures<TextureEvaluator>(TextureEvaluator texEval) { return true; }

        public override BSDF GetBSDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                       ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            return new BSDF(ctx.n, ctx.ns, ctx.dpdus, buf.Alloc(new ThinCoatBxDF(eta)));
        }

        public static string Name() { return "ThinCoatMaterial"; }

        public override FloatTexture GetDisplacement() { return displacement; }
        public override Image GetNormalMap() { return normalMap; }
        public override bool HasSubsurfaceScattering() { return false; }

        public override BSSRDF GetBSSRDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                           ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            return null;
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ ThinCoatMaterial eta: %f ]", eta);
        }

        public static ThinCoatMaterial Create(TextureParameterDictionary parameters, Image normalMap, FileLoc loc, Allocator alloc)
        {
            Float eta = parameters.GetOneFloat("eta", 1.5f);
            FloatTexture displacement = parameters.GetFloatTextureOrNull("displacement", alloc);
            return new ThinCoatMaterial(eta, displacement, normalMap);
        }

        // ThinCoatMaterial Private Data
        private Float eta;
        private FloatTexture displacement;
        private Image normalMap;
    }

    // (not in pbrt-v4) ThinFilmMaterial Definition: thin films on a substrate, a dielectric ("float substrateeta") or a
    // conductor ("spectrum substrateeta" and "spectrum substratek"), with layers from the surface inward listed in "float layereta"
    // (real indices) and "float layerthickness" (nanometres). The surface is smooth unless it is given a roughness, as for
    // "conductor": "float roughness", "uroughness", "vroughness" (default 0), "bool remaproughness" (default true), "string
    // distribution" ("ggx", "beckmann", "studentt") and "float gamma".
    public sealed unsafe class ThinFilmMaterial : Material
    {
        public ThinFilmMaterial(ThinFilmLayers layers, Spectrum substrateEta, SpectrumTexture conductorEta,
                                SpectrumTexture conductorK, FloatTexture displacement, Image normalMap, FloatTexture uRoughness,
                                FloatTexture vRoughness, bool remapRoughness, MicrofacetDistributionType distribution, Float gamma)
        {
            this.uRoughness = uRoughness; this.vRoughness = vRoughness; this.remapRoughness = remapRoughness;
            this.distribution = distribution; this.gamma = gamma;
            this.layers = layers; this.substrateEta = substrateEta;
            this.conductorEta = conductorEta; this.conductorK = conductorK;
            this.displacement = displacement; this.normalMap = normalMap;
        }

        public override bool CanEvaluateTextures<TextureEvaluator>(TextureEvaluator texEval)
        {
            return texEval.CanEvaluate(new FloatTexture[] { uRoughness, vRoughness }, new SpectrumTexture[] { conductorEta, conductorK });
        }

        public override BSDF GetBSDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                       ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            bool conductor = conductorK != null;
            SampledSpectrum etas = conductor ? texEval.Apply(conductorEta, ctx, lambda) : new SampledSpectrum(1.0f);
            SampledSpectrum ks = conductor ? texEval.Apply(conductorK, ctx, lambda) : new SampledSpectrum(0.0f);
            Float uRough = texEval.Apply(uRoughness, ctx), vRough = texEval.Apply(vRoughness, ctx);
            if (remapRoughness)
            {
                uRough = TrowbridgeReitzDistribution.RoughnessToAlpha(uRough);
                vRough = TrowbridgeReitzDistribution.RoughnessToAlpha(vRough);
            }
            MicrofacetDistribution distrib = MicrofacetDistribution.Create(distribution, uRough, vRough, gamma);
            // The index of a dielectric substrate at the point: the light that goes through disperses if it depends on the
            // wavelength. The layers cost no wavelength (thinfilmlayers.cs).
            Float sampledEta = conductor ? 1.5f : ParametricSpectra.SampleIndex(substrateEta, substrateEtaTexture, texEval, ctx, ref lambda);
            return new BSDF(ctx.n, ctx.ns, ctx.dpdus, buf.Alloc(new ThinFilmBxDF(layers.Stack(texEval, ctx, lambda), sampledEta, etas, ks, conductor, distrib)));
        }

        public static string Name() { return "ThinFilmMaterial"; }

        public override FloatTexture GetDisplacement() { return displacement; }
        public override Image GetNormalMap() { return normalMap; }
        public override bool HasSubsurfaceScattering() { return false; }

        public override BSSRDF GetBSSRDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                           ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            return null;
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ ThinFilmMaterial layers: %d ]", layers.Count());
        }

        public static ThinFilmMaterial Create(TextureParameterDictionary parameters, Image normalMap, FileLoc loc, Allocator alloc)
        {
            // The layers, with their dispersion and the texture of their thicknesses (thinfilmlayers.cs)
            ThinFilmLayers layers = ThinFilmLayers.Create(parameters, loc, alloc);
            SpectrumTexture conductorK = parameters.GetSpectrumTextureOrNull("substratek", SpectrumType.Unbounded, alloc);
            SpectrumTexture conductorEta = null;
            if (conductorK != null)
            {
                conductorEta = parameters.GetSpectrumTextureOrNull("substrateeta", SpectrumType.Unbounded, alloc);
                if (conductorEta == null)
                    pbrt.ErrorExit(loc, "thinfilm: a conductor substrate needs \"spectrum substrateeta\" next to \"substratek\".");
            }
            // A dielectric substrate: its index, with the dispersion formulas ("substratesellmeier", "substratecauchy",
            // "substratecauchyb"), or "texture substrateeta": an index that varies over the surface
            Spectrum substrateEta = null;
            SpectrumTexture substrateEtaTexture = null;
            if (conductorK == null)
            {
                substrateEta = ParametricSpectra.Index(parameters, "substrateeta", alloc);
                substrateEtaTexture = ParametricSpectra.IndexTexture(parameters, "substrateeta", substrateEta, alloc);
            }
            if (substrateEta == null)
            {
                substrateEta = new ConstantSpectrum(1.5f);
            }
            FloatTexture displacement = parameters.GetFloatTextureOrNull("displacement", alloc);
            FloatTexture uRoughness = parameters.GetFloatTextureOrNull("uroughness", alloc);
            FloatTexture vRoughness = parameters.GetFloatTextureOrNull("vroughness", alloc);
            if (uRoughness == null)
            {
                uRoughness = parameters.GetFloatTexture("roughness", 0f, alloc);
            }
            if (vRoughness == null)
            {
                vRoughness = parameters.GetFloatTexture("roughness", 0f, alloc);
            }
            bool remapRoughness = parameters.GetOneBool("remaproughness", true);
            MicrofacetDistributionType distribution = MicrofacetDistribution.ParseType(parameters.GetOneString("distribution", "ggx"), loc);
            Float gamma = parameters.GetOneFloat("gamma", 2f);
            ThinFilmMaterial film = new ThinFilmMaterial(layers, substrateEta, conductorEta, conductorK, displacement, normalMap, uRoughness, vRoughness,
                                                         remapRoughness, distribution, gamma);
            film.substrateEtaTexture = substrateEtaTexture;
            return film;
        }

        // The index of a dielectric substrate as a texture, null without "texture substrateeta"
        public SpectrumTexture substrateEtaTexture = null;

        private FloatTexture uRoughness, vRoughness;
        private bool remapRoughness;
        private MicrofacetDistributionType distribution;
        private Float gamma;
        private ThinFilmLayers layers;
        private Spectrum substrateEta;
        private SpectrumTexture conductorEta, conductorK;
        private FloatTexture displacement;
        private Image normalMap;
    }

    // (not in pbrt-v4) InterfaceMaterial Definition: the material of a surface that only separates media. pbrt-v4 has no object for
    // it (the primitive's material is null); this one carries the volume of the material while the scene is built, and the
    // primitives get a null material again (see BasicScene.CreateAggregate()).
    public sealed unsafe class InterfaceMaterial : Material
    {
        public override bool CanEvaluateTextures<TextureEvaluator>(TextureEvaluator texEval) { return true; }

        public override BSDF GetBSDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                       ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            return default;
        }

        public static string Name() { return "InterfaceMaterial"; }

        public override FloatTexture GetDisplacement() { return null; }
        public override Image GetNormalMap() { return null; }
        public override bool HasSubsurfaceScattering() { return false; }

        public override BSSRDF GetBSSRDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                           ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            return null;
        }

        public override string ToString() { return "[ InterfaceMaterial ]"; }
    }

    // ThinDielectricMaterial Definition
    public sealed unsafe class ThinDielectricMaterial : Material
    {
        // ThinDielectricMaterial Public Methods
        public override bool CanEvaluateTextures<TextureEvaluator>(TextureEvaluator texEval) { return true; }

        public ThinDielectricBxDF GetBxDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                            ref SampledWavelengths lambda)
            where TextureEvaluator : ITextureEvaluator
        {
            // Compute index of refraction for dielectric material
            // (not as in pbrt-v4) with "texture eta", the index of the texture at the point
            Float sampledEta = ParametricSpectra.SampleIndex(eta, etaTexture, texEval, ctx, ref lambda);

            // Return BxDF for _ThinDielectricMaterial_
            return new ThinDielectricBxDF(sampledEta);
        }

        public override BSDF GetBSDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                       ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            ThinDielectricBxDF bxdf = GetBxDF(texEval, ctx, ref lambda);
            return new BSDF(ctx.n, ctx.ns, ctx.dpdus, buf.Alloc(bxdf));
        }

        public ThinDielectricMaterial(Spectrum eta, FloatTexture displacement, Image normalMap)
        {
            this.displacement = displacement; this.normalMap = normalMap; this.eta = eta;
        }

        public static string Name() { return "ThinDielectricMaterial"; }

        public override FloatTexture GetDisplacement() { return displacement; }
        public override Image GetNormalMap() { return normalMap; }
        public override bool HasSubsurfaceScattering() { return false; }

        public override BSSRDF GetBSSRDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                           ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            return null;
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ ThinDielectricMaterial displacement: %s normalMap: %s eta: %s ]", displacement,
                                        (normalMap != null ? normalMap.ToString() : "(nullptr)"), eta);
        }

        public static ThinDielectricMaterial Create(TextureParameterDictionary parameters, Image normalMap, FileLoc loc, Allocator alloc)
        {
            // (not as in pbrt-v4) The index, with the dispersion formulas of the C++ fork ("sellmeier", "cauchy", "cauchyb")
            Spectrum eta = ParametricSpectra.Index(parameters, "eta", alloc);
            // (not in pbrt-v4) "texture eta": an index that varies over the surface (a spectrum texture)
            SpectrumTexture etaTexture = ParametricSpectra.IndexTexture(parameters, "eta", eta, alloc);
            if (eta == null)
                eta = new ConstantSpectrum(1.5f);

            FloatTexture displacement = parameters.GetFloatTextureOrNull("displacement", alloc);

            ThinDielectricMaterial thin = new ThinDielectricMaterial(eta, displacement, normalMap);
            thin.etaTexture = etaTexture;
            return thin;
        }

        // (not in pbrt-v4) The index as a texture, null without "texture eta"
        public SpectrumTexture etaTexture = null;

        // ThinDielectricMaterial Private Data
        private FloatTexture displacement;
        private Image normalMap;
        private Spectrum eta;
    }

    // MixMaterial Definition
    public sealed unsafe class MixMaterial : Material
    {
        // MixMaterial Public Methods
        public MixMaterial(Material[] m, FloatTexture amount)
        {
            this.amount = amount;
            materials[0] = m[0];
            materials[1] = m[1];
        }

        public Material ChooseMaterial<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx)
            where TextureEvaluator : ITextureEvaluator
        {
            Float amt = texEval.Apply(amount, ctx);
            if (amt <= 0)
                return materials[0];
            if (amt >= 1)
                return materials[1];
            // (the C++ code hashes the two Material pointers; object identity hashes stand in)
            Float u = pbrt.HashFloat(ctx.p, ctx.wo, (ulong)RuntimeHelpers.GetHashCode(materials[0]),
                                        (ulong)RuntimeHelpers.GetHashCode(materials[1]));
            return (amt < u) ? materials[0] : materials[1];
        }

        public Material GetMaterial(int i) { return materials[i]; }

        public static string Name() { return "MixMaterial"; }

        public override FloatTexture GetDisplacement()
        {
            pbrt.LOG_FATAL("Shouldn't be called");
            return null;
        }

        public override Image GetNormalMap()
        {
            pbrt.LOG_FATAL("Shouldn't be called");
            return null;
        }
        public override bool HasSubsurfaceScattering() { return false; }

        public override BSSRDF GetBSSRDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                           ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            pbrt.LOG_FATAL("Shouldn't be called");
            return null;
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ MixMaterial materials: [ %s %s ] amount: %s ]", materials[0], materials[1], amount);
        }

        public static MixMaterial Create(Material[] materials, TextureParameterDictionary parameters, FileLoc loc, Allocator alloc)
        {
            FloatTexture amount = parameters.GetFloatTexture("amount", 0.5f, alloc);

            // Check for this stuff here, where we can include the FileLoc in
            // the error message. Note that both of these limitations could be
            // relaxed if they were problematic; the issue is that we currently
            // resolve MixMaterials in the closest hit shader...
            if (pbrt.Options.wavefront && !new BasicTextureEvaluator().CanEvaluate(new FloatTexture[] { amount }, new SpectrumTexture[0]))
                pbrt.ErrorExit(loc, "The wavefront renderer currently only supports basic textures " +
                                       "for its \"amount\" parameter.");

            return new MixMaterial(materials, amount);
        }

        public override bool CanEvaluateTextures<TextureEvaluator>(TextureEvaluator texEval)
        {
            return texEval.CanEvaluate(new FloatTexture[] { amount }, new SpectrumTexture[0]);
        }

        public override BSDF GetBSDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                       ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            // (ConcreteBxDF is void: "return BSDF();")
            pbrt.LOG_FATAL("MixMaterial::GetBxDF() shouldn't be called");
            return default;
        }

        // MixMaterial Private Members
        private FloatTexture amount;
        private Material[] materials = new Material[2];
    }

    // HairMaterial Definition
    public sealed unsafe class HairMaterial : Material
    {
        // HairMaterial Public Methods
        public HairMaterial(SpectrumTexture sigma_a, SpectrumTexture color, FloatTexture eumelanin, FloatTexture pheomelanin,
                            FloatTexture eta, FloatTexture beta_m, FloatTexture beta_n, FloatTexture alpha)
        {
            this.sigma_a = sigma_a; this.color = color; this.eumelanin = eumelanin; this.pheomelanin = pheomelanin;
            this.eta = eta; this.beta_m = beta_m; this.beta_n = beta_n; this.alpha = alpha;
        }

        public static string Name() { return "HairMaterial"; }

        public override bool CanEvaluateTextures<TextureEvaluator>(TextureEvaluator texEval)
        {
            return texEval.CanEvaluate(new FloatTexture[] { eumelanin, pheomelanin, eta, beta_m, beta_n, alpha },
                                       new SpectrumTexture[] { sigma_a, color });
        }

        public HairBxDF GetBxDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx, ref SampledWavelengths lambda)
            where TextureEvaluator : ITextureEvaluator
        {
            Float bm = std.max<Float>(1e-2f, std.min<Float>(1.0f, texEval.Apply(beta_m, ctx)));
            Float bn = std.max<Float>(1e-2f, std.min<Float>(1.0f, texEval.Apply(beta_n, ctx)));
            Float a = texEval.Apply(alpha, ctx);
            Float e = texEval.Apply(eta, ctx);

            SampledSpectrum sig_a;
            if (sigma_a != null)
                sig_a = pbrt.ClampZero(texEval.Apply(sigma_a, ctx, lambda));
            else if (color != null)
            {
                SampledSpectrum c = pbrt.Clamp(texEval.Apply(color, ctx, lambda), 0, 1);
                sig_a = HairBxDF.SigmaAFromReflectance(c, bn, lambda);
            }
            else
            {
                pbrt.CHECK(eumelanin != null || pheomelanin != null);
                sig_a = HairBxDF.SigmaAFromConcentration(
                            std.max<Float>(0, eumelanin != null ? texEval.Apply(eumelanin, ctx) : 0),
                            std.max<Float>(0, pheomelanin != null ? texEval.Apply(pheomelanin, ctx) : 0))
                            .Sample(lambda);
            }

            // Offset along width
            Float h = -1 + 2 * ctx.uv[1];
            return new HairBxDF(h, e, sig_a, bm, bn, a);
        }

        public override BSDF GetBSDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                       ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            HairBxDF bxdf = GetBxDF(texEval, ctx, ref lambda);
            return new BSDF(ctx.n, ctx.ns, ctx.dpdus, buf.Alloc(bxdf));
        }

        public override FloatTexture GetDisplacement() { return null; }
        public override bool HasSubsurfaceScattering() { return false; }
        public override Image GetNormalMap() { return null; }

        public override BSSRDF GetBSSRDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                           ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            return null;
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ HairMaterial sigma_a: %s color: %s eumelanin: %s " +
                                        "pheomelanin: %s eta: %s beta_m: %s beta_n: %s alpha: %s ]",
                                        sigma_a, color, eumelanin, pheomelanin, eta, beta_m, beta_n, alpha);
        }

        public static HairMaterial Create(TextureParameterDictionary parameters, FileLoc loc, Allocator alloc)
        {
            SpectrumTexture sigma_a = parameters.GetSpectrumTextureOrNull("sigma_a", SpectrumType.Unbounded, alloc);
            SpectrumTexture reflectance = parameters.GetSpectrumTextureOrNull("reflectance", SpectrumType.Albedo, alloc);
            if (reflectance == null)
                reflectance = parameters.GetSpectrumTextureOrNull("color", SpectrumType.Albedo, alloc);
            FloatTexture eumelanin = parameters.GetFloatTextureOrNull("eumelanin", alloc);
            FloatTexture pheomelanin = parameters.GetFloatTextureOrNull("pheomelanin", alloc);
            if (sigma_a != null)
            {
                if (reflectance != null)
                    pbrt.Warning(loc, "Ignoring \"reflectance\" parameter since \"sigma_a\" was provided.");
                if (eumelanin != null)
                    pbrt.Warning(loc, "Ignoring \"eumelanin\" parameter since \"sigma_a\" was provided.");
                if (pheomelanin != null)
                    pbrt.Warning(loc, "Ignoring \"pheomelanin\" parameter since \"sigma_a\" was provided.");
            }
            else if (reflectance != null)
            {
                if (sigma_a != null)
                    pbrt.Warning(loc, "Ignoring \"sigma_a\" parameter since \"reflectance\" was provided.");
                if (eumelanin != null)
                    pbrt.Warning(loc, "Ignoring \"eumelanin\" parameter since \"reflectance\" was provided.");
                if (pheomelanin != null)
                    pbrt.Warning(loc, "Ignoring \"pheomelanin\" parameter since \"reflectance\" was provided.");
            }
            else if (eumelanin != null || pheomelanin != null)
            {
                if (sigma_a != null)
                    pbrt.Warning(loc, "Ignoring \"sigma_a\" parameter since \"eumelanin\"/\"pheomelanin\" was provided.");
                if (reflectance != null)
                    pbrt.Warning(loc, "Ignoring \"reflectance\" parameter since \"eumelanin\"/\"pheomelanin\" was provided.");
            }
            else
            {
                // Default: brown-ish hair.
                sigma_a = new SpectrumConstantTexture(HairBxDF.SigmaAFromConcentration(1.3f, 0f));
            }

            FloatTexture eta = parameters.GetFloatTexture("eta", 1.55f, alloc);
            FloatTexture beta_m = parameters.GetFloatTexture("beta_m", 0.3f, alloc);
            FloatTexture beta_n = parameters.GetFloatTexture("beta_n", 0.3f, alloc);
            FloatTexture alpha = parameters.GetFloatTexture("alpha", 2f, alloc);

            return new HairMaterial(sigma_a, reflectance, eumelanin, pheomelanin, eta, beta_m, beta_n, alpha);
        }

        // HairMaterial Private Data
        private SpectrumTexture sigma_a, color;
        private FloatTexture eumelanin, pheomelanin, eta;
        private FloatTexture beta_m, beta_n, alpha;
    }

    // DiffuseMaterial Definition
    public sealed unsafe class DiffuseMaterial : Material
    {
        // DiffuseMaterial Public Methods
        public static string Name() { return "DiffuseMaterial"; }

        public override FloatTexture GetDisplacement() { return displacement; }
        public override Image GetNormalMap() { return normalMap; }
        public override bool HasSubsurfaceScattering() { return false; }

        public override BSSRDF GetBSSRDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                           ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            return null;
        }

        public DiffuseMaterial(SpectrumTexture reflectance, FloatTexture displacement, Image normalMap)
        {
            this.normalMap = normalMap; this.displacement = displacement; this.reflectance = reflectance;
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ DiffuseMaterial displacement: %s normapMap: %s reflectance: %s ]", displacement,
                                        (normalMap != null ? normalMap.ToString() : "(nullptr)"), reflectance);
        }

        public static DiffuseMaterial Create(TextureParameterDictionary parameters, Image normalMap, FileLoc loc, Allocator alloc)
        {
            SpectrumTexture reflectance = parameters.GetSpectrumTexture("reflectance", null, SpectrumType.Albedo, alloc);
            if (reflectance == null)
                reflectance = new SpectrumConstantTexture(new ConstantSpectrum(0.5f));

            FloatTexture displacement = parameters.GetFloatTextureOrNull("displacement", alloc);

            return new DiffuseMaterial(reflectance, displacement, normalMap);
        }

        public override bool CanEvaluateTextures<TextureEvaluator>(TextureEvaluator texEval)
        {
            return texEval.CanEvaluate(new FloatTexture[0], new SpectrumTexture[] { reflectance });
        }

        // (The EON and VMF lobes that the C++ fork put in this material are the materials "eondiffuse" and "vmfdiffuse".)
        public DiffuseBxDF GetBxDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx, ref SampledWavelengths lambda)
            where TextureEvaluator : ITextureEvaluator
        {
            SampledSpectrum r = pbrt.Clamp(texEval.Apply(reflectance, ctx, lambda), 0, 1);
            return new DiffuseBxDF(r);
        }

        public override BSDF GetBSDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                       ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            DiffuseBxDF bxdf = GetBxDF(texEval, ctx, ref lambda);
            return new BSDF(ctx.n, ctx.ns, ctx.dpdus, buf.Alloc(bxdf));
        }

        // DiffuseMaterial Private Members
        private Image normalMap;
        private FloatTexture displacement;
        private SpectrumTexture reflectance;
    }

    // EonDiffuseMaterial Definition
    // (not in pbrt-v4) The material "eondiffuse": the energy-preserving Oren-Nayar BRDF of Portsmouth, Kutz and Hill
    // (EonDiffuseBxDF) on its own. It is the rough diffuse lobe of OpenPBR.
    //   spectrum reflectance   the single scattering albedo (rho of the paper). Default 0.5
    //   float roughness        0 (Lambertian) to 1; a texture. Default 0.5
    public sealed unsafe class EonDiffuseMaterial : Material
    {
        // EonDiffuseMaterial Public Methods
        public static string Name() { return "EonDiffuseMaterial"; }

        public override FloatTexture GetDisplacement() { return displacement; }
        public override Image GetNormalMap() { return normalMap; }
        public override bool HasSubsurfaceScattering() { return false; }

        public override BSSRDF GetBSSRDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                           ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            return null;
        }

        public EonDiffuseMaterial(SpectrumTexture reflectance, FloatTexture roughness, FloatTexture displacement, Image normalMap)
        {
            this.normalMap = normalMap;
            this.displacement = displacement;
            this.reflectance = reflectance;
            this.roughness = roughness;
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ EonDiffuseMaterial displacement: %s normapMap: %s reflectance: %s roughness: %s ]", displacement,
                                     (normalMap != null ? normalMap.ToString() : "(nullptr)"), reflectance, roughness);
        }

        public static EonDiffuseMaterial Create(TextureParameterDictionary parameters, Image normalMap, FileLoc loc, Allocator alloc)
        {
            SpectrumTexture reflectance = parameters.GetSpectrumTexture("reflectance", null, SpectrumType.Albedo, alloc);
            if (reflectance == null)
            {
                reflectance = new SpectrumConstantTexture(new ConstantSpectrum(0.5f));
            }
            FloatTexture roughness = parameters.GetFloatTexture("roughness", 0.5f, alloc);
            FloatTexture displacement = parameters.GetFloatTextureOrNull("displacement", alloc);

            return new EonDiffuseMaterial(reflectance, roughness, displacement, normalMap);
        }

        public override bool CanEvaluateTextures<TextureEvaluator>(TextureEvaluator texEval)
        {
            return texEval.CanEvaluate(new FloatTexture[] { roughness }, new SpectrumTexture[] { reflectance });
        }

        // The lobe is Lambertian where the roughness is 0 (the model reduces to it)
        public BxDF GetBxDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx, ref SampledWavelengths lambda)
            where TextureEvaluator : ITextureEvaluator
        {
            SampledSpectrum r = pbrt.Clamp(texEval.Apply(reflectance, ctx, lambda), 0, 1);
            Float rough = pbrt.Clamp(texEval.Apply(roughness, ctx), 0, 1);
            if (rough == 0)
            {
                return new DiffuseBxDF(r);
            }
            return new EonDiffuseBxDF(r, rough);
        }

        public override BSDF GetBSDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                       ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            BxDF bxdf = GetBxDF(texEval, ctx, ref lambda);
            return new BSDF(ctx.n, ctx.ns, ctx.dpdus, buf.Alloc(bxdf));
        }

        // EonDiffuseMaterial Private Members
        private Image normalMap;
        private FloatTexture displacement;
        private SpectrumTexture reflectance;
        private FloatTexture roughness;
    }

    // VmfDiffuseMaterial Definition
    // (not in pbrt-v4) The material "vmfdiffuse": the VMF Diffuse BRDF of d'Eon and Weidlich (VmfDiffuseBxDF) on its own. It is
    // kept out of the other materials because its fits are not energy conserving to better than a few percent (a white surface
    // reflects between 0.91 and 1.06), which a layered material would compound.
    //   spectrum reflectance   the colour seen (the albedo mapping of the paper is applied). Default 0.5
    //   float roughness        0 (Lambertian) to 1 (the Lambert-sphere BRDF); a texture. Default 0.5
    public sealed unsafe class VmfDiffuseMaterial : Material
    {
        // VmfDiffuseMaterial Public Methods
        public static string Name() { return "VmfDiffuseMaterial"; }

        public override FloatTexture GetDisplacement() { return displacement; }
        public override Image GetNormalMap() { return normalMap; }
        public override bool HasSubsurfaceScattering() { return false; }

        public override BSSRDF GetBSSRDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                           ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            return null;
        }

        public VmfDiffuseMaterial(SpectrumTexture reflectance, FloatTexture roughness, FloatTexture displacement, Image normalMap)
        {
            this.normalMap = normalMap;
            this.displacement = displacement;
            this.reflectance = reflectance;
            this.roughness = roughness;
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ VmfDiffuseMaterial displacement: %s normapMap: %s reflectance: %s roughness: %s ]", displacement,
                                     (normalMap != null ? normalMap.ToString() : "(nullptr)"), reflectance, roughness);
        }

        public static VmfDiffuseMaterial Create(TextureParameterDictionary parameters, Image normalMap, FileLoc loc, Allocator alloc)
        {
            SpectrumTexture reflectance = parameters.GetSpectrumTexture("reflectance", null, SpectrumType.Albedo, alloc);
            if (reflectance == null)
            {
                reflectance = new SpectrumConstantTexture(new ConstantSpectrum(0.5f));
            }
            FloatTexture roughness = parameters.GetFloatTexture("roughness", 0.5f, alloc);
            FloatTexture displacement = parameters.GetFloatTextureOrNull("displacement", alloc);

            return new VmfDiffuseMaterial(reflectance, roughness, displacement, normalMap);
        }

        public override bool CanEvaluateTextures<TextureEvaluator>(TextureEvaluator texEval)
        {
            return texEval.CanEvaluate(new FloatTexture[] { roughness }, new SpectrumTexture[] { reflectance });
        }

        // The lobe is Lambertian where the roughness is 0, as in the paper
        public BxDF GetBxDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx, ref SampledWavelengths lambda)
            where TextureEvaluator : ITextureEvaluator
        {
            SampledSpectrum r = pbrt.Clamp(texEval.Apply(reflectance, ctx, lambda), 0, 1);
            Float rough = pbrt.Clamp(texEval.Apply(roughness, ctx), 0, 1);
            if (rough == 0)
            {
                return new DiffuseBxDF(r);
            }
            return new VmfDiffuseBxDF(r, rough);
        }

        public override BSDF GetBSDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                       ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            BxDF bxdf = GetBxDF(texEval, ctx, ref lambda);
            return new BSDF(ctx.n, ctx.ns, ctx.dpdus, buf.Alloc(bxdf));
        }

        // VmfDiffuseMaterial Private Members
        private Image normalMap;
        private FloatTexture displacement;
        private SpectrumTexture reflectance;
        private FloatTexture roughness;
    }

    // MicrosurfaceKind Definition
    public enum MicrosurfaceKind
    {
        MultiScatterConductor,
        MultiScatterDielectric,
        MultiScatterDiffuse
    }

    // MicrosurfaceMaterial Definition
    // (not in pbrt-v4) The materials of the microsurfaces whose light meets more than one microfacet. Each is a material of its
    // own name, and they share this class:
    //   "multiscatterconductor"   MultiScatterConductorBxDF: the Smith microsurface of a conductor with its multiple scattering
    //                             (Heitz et al. 2016)
    //   "multiscatterdielectric"  MultiScatterDielectricBxDF: the same for a dielectric interface
    //   "multiscatterdiffuse"     MultiScatterDiffuseBxDF: the same for Lambertian microfacets (a rough diffuse surface)
    // Parameters:
    //   float roughness, uroughness, vroughness   as for "conductor"; default 0.3
    //   bool remaproughness                       as for "conductor"; default true
    //   string distribution                       "ggx" (default), "beckmann", "studentt"
    //   float gamma                               the shape of "studentt"; default 2
    //   integer scatterdepth                      the reflections a walk may make; default 32
    //   spectrum eta, k, or spectrum reflectance  the conductors, as for "conductor"
    //   float or spectrum eta                     "multiscatterdielectric", as for "dielectric"; default 1.5
    //   spectrum reflectance                      "multiscatterdiffuse": the reflectance of a microfacet; default 0.5
    //   float layereta, layerthickness            thin films on the microfacets of the conductor and of the dielectric, from the
    //                                             surface inward, as for "thinfilm" (real indices, nanometres); default none
    // A microsurface that is effectively smooth is the specular BxDF of pbrt (or the Lambertian one).
    public sealed unsafe class MicrosurfaceMaterial : Material
    {
        // MicrosurfaceMaterial Public Methods
        public MicrosurfaceMaterial(MicrosurfaceKind kind, SpectrumTexture eta, SpectrumTexture k, SpectrumTexture reflectance, Spectrum dielectricEta,
                                    FloatTexture uRoughness, FloatTexture vRoughness, FloatTexture displacement, Image normalMap, bool remapRoughness,
                                    MicrofacetDistributionType distribution, Float gamma, int depth, ThinFilmLayers layers)
        {
            this.layers = layers;
            this.kind = kind;
            this.eta = eta;
            this.k = k;
            this.reflectance = reflectance;
            this.dielectricEta = dielectricEta;
            this.uRoughness = uRoughness;
            this.vRoughness = vRoughness;
            this.displacement = displacement;
            this.normalMap = normalMap;
            this.remapRoughness = remapRoughness;
            this.distribution = distribution;
            this.gamma = gamma;
            this.depth = depth;
        }

        public static string Name() { return "MicrosurfaceMaterial"; }

        public override FloatTexture GetDisplacement() { return displacement; }
        public override Image GetNormalMap() { return normalMap; }
        public override bool HasSubsurfaceScattering() { return false; }

        public override BSSRDF GetBSSRDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                           ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            return null;
        }

        public override bool CanEvaluateTextures<TextureEvaluator>(TextureEvaluator texEval)
        {
            return texEval.CanEvaluate(new FloatTexture[] { uRoughness, vRoughness }, new SpectrumTexture[] { eta, k, reflectance });
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ MicrosurfaceMaterial kind: %s displacement: %s normalMap: %s eta: %s k: %s reflectance: %s dielectricEta: %s " +
                                     "uRoughness: %s vRoughness: %s remapRoughness: %s distribution: %s gamma: %f depth: %d ]", kind.ToString(), displacement,
                                     (normalMap != null ? normalMap.ToString() : "(nullptr)"), eta, k, reflectance, dielectricEta, uRoughness, vRoughness,
                                     remapRoughness, distribution.ToString(), gamma, depth);
        }

        public static MicrosurfaceMaterial Create(MicrosurfaceKind kind, TextureParameterDictionary parameters, Image normalMap, FileLoc loc, Allocator alloc)
        {
            SpectrumTexture eta = null, k = null, reflectance = null;
            Spectrum dielectricEta = null;
            SpectrumTexture dielectricEtaTexture = null;
            bool conductor = kind == MicrosurfaceKind.MultiScatterConductor;
            if (conductor)
            {
                eta = parameters.GetSpectrumTextureOrNull("eta", SpectrumType.Unbounded, alloc);
                k = parameters.GetSpectrumTextureOrNull("k", SpectrumType.Unbounded, alloc);
                reflectance = parameters.GetSpectrumTextureOrNull("reflectance", SpectrumType.Albedo, alloc);
                if (reflectance != null && (eta != null || k != null))
                {
                    pbrt.ErrorExit(loc, "For a conductor, both \"reflectance\" and \"eta\" and \"k\" can't be provided.");
                }
                if (reflectance == null)
                {
                    if (eta == null)
                    {
                        eta = new SpectrumConstantTexture(pbrt.GetNamedSpectrum("metal-Cu-eta"));
                    }
                    if (k == null)
                    {
                        k = new SpectrumConstantTexture(pbrt.GetNamedSpectrum("metal-Cu-k"));
                    }
                }
            }
            else if (kind == MicrosurfaceKind.MultiScatterDielectric)
            {
                // (not as in pbrt-v4) The index, with the dispersion formulas of the C++ fork, or "texture eta"
                dielectricEta = ParametricSpectra.Index(parameters, "eta", alloc);
                dielectricEtaTexture = ParametricSpectra.IndexTexture(parameters, "eta", dielectricEta, alloc);
                if (dielectricEta == null)
                {
                    dielectricEta = new ConstantSpectrum(1.5f);
                }
            }
            else
            {
                reflectance = parameters.GetSpectrumTexture("reflectance", null, SpectrumType.Albedo, alloc);
                if (reflectance == null)
                {
                    reflectance = new SpectrumConstantTexture(new ConstantSpectrum(0.5f));
                }
            }

            FloatTexture uRoughness = parameters.GetFloatTextureOrNull("uroughness", alloc);
            FloatTexture vRoughness = parameters.GetFloatTextureOrNull("vroughness", alloc);
            if (uRoughness == null)
            {
                uRoughness = parameters.GetFloatTexture("roughness", 0.3f, alloc);
            }
            if (vRoughness == null)
            {
                vRoughness = parameters.GetFloatTexture("roughness", 0.3f, alloc);
            }
            FloatTexture displacement = parameters.GetFloatTextureOrNull("displacement", alloc);
            bool remapRoughness = parameters.GetOneBool("remaproughness", true);
            MicrofacetDistributionType distribution = MicrofacetDistribution.ParseType(parameters.GetOneString("distribution", "ggx"), loc);
            Float gamma = parameters.GetOneFloat("gamma", 2f);
            int depth = parameters.GetOneInt("scatterdepth", 32);
            // The films on the microfacets, with their dispersion and the texture of their thicknesses (thinfilmlayers.cs)
            ThinFilmLayers layers = kind != MicrosurfaceKind.MultiScatterDiffuse ? ThinFilmLayers.Create(parameters, loc, alloc) : new ThinFilmLayers();
            MicrosurfaceMaterial microsurface = new MicrosurfaceMaterial(kind, eta, k, reflectance, dielectricEta, uRoughness, vRoughness, displacement, normalMap, remapRoughness,
                                            distribution, gamma, depth, layers);
            microsurface.dielectricEtaTexture = dielectricEtaTexture;
            return microsurface;
        }

        public BxDF GetBxDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx, ref SampledWavelengths lambda)
            where TextureEvaluator : ITextureEvaluator
        {
            Float uRough = texEval.Apply(uRoughness, ctx), vRough = texEval.Apply(vRoughness, ctx);
            if (remapRoughness)
            {
                uRough = TrowbridgeReitzDistribution.RoughnessToAlpha(uRough);
                vRough = TrowbridgeReitzDistribution.RoughnessToAlpha(vRough);
            }
            MicrofacetDistribution distrib = MicrofacetDistribution.Create(distribution, uRough, vRough, gamma);
            MicrosurfaceHeight height = new MicrosurfaceHeightUniform();
            switch (kind)
            {
            case MicrosurfaceKind.MultiScatterDielectric:
            {
                // (not as in pbrt-v4) with "texture eta", the index of the texture at the point
                Float sampledEta = ParametricSpectra.SampleIndex(dielectricEta, dielectricEtaTexture, texEval, ctx, ref lambda);
                if (layers.Count() > 0)
                {
                    // The films: the smooth surface, or one whose index bends nothing, is the thin film BxDF
                    if (distrib.EffectivelySmooth() || sampledEta == 1)
                    {
                        return new ThinFilmBxDF(layers.Stack(texEval, ctx, lambda), sampledEta, new SampledSpectrum(1f), new SampledSpectrum(0f), false,
                                                distrib);
                    }
                    return new MultiScatterDielectricBxDF(distrib, height, sampledEta, depth, layers.Stack(texEval, ctx, lambda));
                }
                if (distrib.EffectivelySmooth() || sampledEta == 1)
                {
                    return new DielectricBxDF(sampledEta, distrib);
                }
                return new MultiScatterDielectricBxDF(distrib, height, sampledEta, depth);
            }
            case MicrosurfaceKind.MultiScatterDiffuse:
            {
                SampledSpectrum r = pbrt.Clamp(texEval.Apply(reflectance, ctx, lambda), 0, 1);
                if (distrib.EffectivelySmooth())
                {
                    return new DiffuseBxDF(r);
                }
                return new MultiScatterDiffuseBxDF(distrib, height, r, depth);
            }
            default:
            {
                SampledSpectrum etas, ks;
                if (eta != null)
                {
                    etas = texEval.Apply(eta, ctx, lambda);
                    ks = texEval.Apply(k, ctx, lambda);
                }
                else
                {
                    // Avoid r==0 NaN case...
                    SampledSpectrum r = pbrt.Clamp(texEval.Apply(reflectance, ctx, lambda), 0, .9999f);
                    etas = new SampledSpectrum(1f);
                    ks = 2 * pbrt.Sqrt(r) / pbrt.Sqrt(pbrt.ClampZero(new SampledSpectrum(1f) - r));
                }
                if (layers.Count() > 0)
                {
                    if (distrib.EffectivelySmooth())
                    {
                        return new ThinFilmBxDF(layers.Stack(texEval, ctx, lambda), 1.5f, etas, ks, true, distrib);
                    }
                    return new MultiScatterConductorBxDF(distrib, height, etas, ks, depth, layers.Stack(texEval, ctx, lambda));
                }
                if (distrib.EffectivelySmooth())
                {
                    return new ConductorBxDF(distrib, etas, ks);
                }
                return new MultiScatterConductorBxDF(distrib, height, etas, ks, depth);
            }
            }
        }

        public override BSDF GetBSDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                       ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            BxDF bxdf = GetBxDF(texEval, ctx, ref lambda);
            return new BSDF(ctx.n, ctx.ns, ctx.dpdus, buf.Alloc(bxdf));
        }

        // MicrosurfaceMaterial Private Members
        private MicrosurfaceKind kind;
        private SpectrumTexture eta, k, reflectance;
        private Spectrum dielectricEta;
        // (not in pbrt-v4) The index of the dielectric as a texture, null without "texture eta"
        public SpectrumTexture dielectricEtaTexture = null;
        private FloatTexture uRoughness, vRoughness;
        private FloatTexture displacement;
        private Image normalMap;
        private bool remapRoughness;
        private MicrofacetDistributionType distribution;
        private Float gamma;
        private int depth;
        private ThinFilmLayers layers;
    }

    // PearlescentMaterial Definition
    // (not in pbrt-v4) The pearlescent pigment of Guillen et al. 2020 (see PearlescentBxDF), material "pearlescent":
    //   float kappa                 the density of the platelets times the thickness of the slab (its optical depth scale); 3
    //   float roughness, uroughness, vroughness
    //                               the spread of the normals of the platelets (the alpha of a Trowbridge-Reitz distribution); 0.2
    //   spectrum reflectance        the albedo of the base; 0.2
    //   float containern            the index of the container; 1.57
    //   float coatn, coatabbe, coatthickness
    //                               the coating of a platelet: index at 587.6 nm, Abbe number, thickness in nm; 2.5, 9, 60
    //   float substraten, substrateabbe, substratethickness, substratestd
    //                               the substrate of a platelet, and the standard deviation of its thickness in nm; 1.58, 63, 500, 60
    //   string distribution         the distribution of the normals as a MicrofacetDistribution ("ggx", "beckmann", ...; see
    //                               MicrofacetDistribution.ParseType); not given: Trowbridge-Reitz, written out in the BxDF
    //   float gamma                 the exponent of the Student-t distribution; 2
    //   integer maxevents           the scattering events of a path in the slab (1: single scattering only); 64
    //   integer quadrature          the samples of the effective extinction; 16
    public sealed unsafe class PearlescentMaterial : Material
    {
        // PearlescentMaterial Public Methods
        public PearlescentMaterial(PearlescentStack stack, Float kappa, FloatTexture uRoughness, FloatTexture vRoughness, SpectrumTexture reflectance,
                                   FloatTexture displacement, Image normalMap, int maxEvents, int quadrature, bool general,
                                   MicrofacetDistributionType distribution, Float gamma)
        {
            this.general = general;
            this.distribution = distribution;
            this.gamma = gamma;
            this.stack = stack;
            this.kappa = kappa;
            this.uRoughness = uRoughness;
            this.vRoughness = vRoughness;
            this.reflectance = reflectance;
            this.displacement = displacement;
            this.normalMap = normalMap;
            this.maxEvents = maxEvents;
            this.quadrature = quadrature;
        }

        public static string Name() { return "PearlescentMaterial"; }

        public override FloatTexture GetDisplacement() { return displacement; }
        public override Image GetNormalMap() { return normalMap; }
        public override bool HasSubsurfaceScattering() { return false; }

        public override BSSRDF GetBSSRDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                           ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            return null;
        }

        public override bool CanEvaluateTextures<TextureEvaluator>(TextureEvaluator texEval)
        {
            return texEval.CanEvaluate(new FloatTexture[] { uRoughness, vRoughness }, new SpectrumTexture[] { reflectance });
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ PearlescentMaterial kappa: %f maxEvents: %d ]", kappa, maxEvents);
        }

        public static PearlescentMaterial Create(TextureParameterDictionary parameters, Image normalMap, FileLoc loc, Allocator alloc)
        {
            PearlescentStack stack = new PearlescentStack();
            stack.containerEta = parameters.GetOneFloat("containern", 1.57f);
            stack.coatingEta = parameters.GetOneFloat("coatn", 2.5f);
            stack.coatingAbbe = parameters.GetOneFloat("coatabbe", 9f);
            // (the thicknesses can also be float textures: they are read below)
            stack.coatingThickness = 60;
            stack.substrateEta = parameters.GetOneFloat("substraten", 1.58f);
            stack.substrateAbbe = parameters.GetOneFloat("substrateabbe", 63f);
            stack.substrateThickness = 500;
            stack.substrateDeviation = parameters.GetOneFloat("substratestd", 60f);
            FloatTexture uRoughness = parameters.GetFloatTextureOrNull("uroughness", alloc);
            FloatTexture vRoughness = parameters.GetFloatTextureOrNull("vroughness", alloc);
            if (uRoughness == null)
            {
                uRoughness = parameters.GetFloatTexture("roughness", 0.2f, alloc);
            }
            if (vRoughness == null)
            {
                vRoughness = parameters.GetFloatTexture("roughness", 0.2f, alloc);
            }
            SpectrumTexture reflectance = parameters.GetSpectrumTexture("reflectance", null, SpectrumType.Albedo, alloc);
            if (reflectance == null)
            {
                reflectance = new SpectrumConstantTexture(new ConstantSpectrum(0.2f));
            }
            FloatTexture displacement = parameters.GetFloatTextureOrNull("displacement", alloc);
            string name = parameters.GetOneString("distribution", "");
            bool general = name != "";
            MicrofacetDistributionType distribution = MicrofacetDistribution.ParseType(general ? name : "ggx", loc);
            PearlescentMaterial pearl = new PearlescentMaterial(stack, parameters.GetOneFloat("kappa", 3f), uRoughness, vRoughness, reflectance, displacement,
                                                                normalMap, parameters.GetOneInt("maxevents", 64), parameters.GetOneInt("quadrature", 16), general,
                                                                distribution, parameters.GetOneFloat("gamma", 2f));
            // "coatthickness" and "substratethickness" in nanometres, numbers or float textures: the colour of the platelets
            // follows them over the surface
            pearl.coatingThickness = parameters.GetFloatTexture("coatthickness", 60f, alloc);
            pearl.substrateThickness = parameters.GetFloatTexture("substratethickness", 500f, alloc);
            return pearl;
        }

        public PearlescentBxDF GetBxDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx, ref SampledWavelengths lambda)
            where TextureEvaluator : ITextureEvaluator
        {
            Float alphaX = std.max<Float>(texEval.Apply(uRoughness, ctx), 1e-3f);
            Float alphaY = std.max<Float>(texEval.Apply(vRoughness, ctx), 1e-3f);
            SampledSpectrum r = pbrt.Clamp(texEval.Apply(reflectance, ctx, lambda), 0, 1);
            SampledSpectrum wavelengths = new SampledSpectrum(0f);
            for (int j = 0; j < NSpectrumSamples; ++j)
            {
                wavelengths[j] = lambda[j];
            }
            PearlescentStack stack = this.stack;
            stack.coatingThickness = std.max<Float>(0, texEval.Apply(coatingThickness, ctx));
            stack.substrateThickness = std.max<Float>(0, texEval.Apply(substrateThickness, ctx));
            if (general)
            {
                return new PearlescentBxDF(stack, wavelengths, kappa, MicrofacetDistribution.Create(distribution, alphaX, alphaY, gamma), r, quadrature,
                                           maxEvents);
            }
            return new PearlescentBxDF(stack, wavelengths, kappa, alphaX, alphaY, r, quadrature, maxEvents);
        }

        public override BSDF GetBSDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                       ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            PearlescentBxDF bxdf = GetBxDF(texEval, ctx, ref lambda);
            return new BSDF(ctx.n, ctx.ns, ctx.dpdus, buf.Alloc(bxdf));
        }

        // The thicknesses of the coating and of the substrate of a platelet at a point, in nanometres
        public FloatTexture coatingThickness, substrateThickness;

        // PearlescentMaterial Private Members
        private PearlescentStack stack;
        private Float kappa;
        private FloatTexture uRoughness, vRoughness;
        private SpectrumTexture reflectance;
        private FloatTexture displacement;
        private Image normalMap;
        private int maxEvents, quadrature;
        private bool general;
        private MicrofacetDistributionType distribution;
        private Float gamma;
    }

    // ConductorMaterial Definition
    public sealed unsafe class ConductorMaterial : Material
    {
        // ConductorMaterial Public Methods
        public override bool CanEvaluateTextures<TextureEvaluator>(TextureEvaluator texEval)
        {
            return texEval.CanEvaluate(new FloatTexture[] { uRoughness, vRoughness }, new SpectrumTexture[] { eta, k, reflectance });
        }

        public ConductorBxDF GetBxDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx, ref SampledWavelengths lambda)
            where TextureEvaluator : ITextureEvaluator
        {
            // Return BSDF for _ConductorMaterial_
            Float uRough = texEval.Apply(uRoughness, ctx), vRough = texEval.Apply(vRoughness, ctx);
            if (remapRoughness)
            {
                uRough = TrowbridgeReitzDistribution.RoughnessToAlpha(uRough);
                vRough = TrowbridgeReitzDistribution.RoughnessToAlpha(vRough);
            }
            SampledSpectrum etas, ks;
            if (eta != null)
            {
                etas = texEval.Apply(eta, ctx, lambda);
                ks = texEval.Apply(k, ctx, lambda);
            }
            else
            {
                // Avoid r==0 NaN case...
                SampledSpectrum r = pbrt.Clamp(texEval.Apply(reflectance, ctx, lambda), 0, .9999f);
                etas = new SampledSpectrum(1f);
                ks = 2 * pbrt.Sqrt(r) / pbrt.Sqrt(pbrt.ClampZero(new SampledSpectrum(1f) - r));
            }
            MicrofacetDistribution distrib = MicrofacetDistribution.Create(distribution, uRough, vRough, gamma);
            return new ConductorBxDF(distrib, etas, ks);
        }

        public override BSDF GetBSDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                       ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            ConductorBxDF bxdf = GetBxDF(texEval, ctx, ref lambda);
            if (retroreflectivity > 0)
            {
                return new BSDF(ctx.n, ctx.ns, ctx.dpdus, buf.Alloc(new RetroreflectiveBxDF(bxdf, retroreflectivity)));
            }
            return new BSDF(ctx.n, ctx.ns, ctx.dpdus, buf.Alloc(bxdf));
        }

        // (not in pbrt-v4) "float retroreflectivity": the share of the lobe that is evaluated at the mirrored view direction
        // (RetroreflectiveBxDF); 0 = off
        public Float retroreflectivity = 0;

        public ConductorMaterial(SpectrumTexture eta, SpectrumTexture k, SpectrumTexture reflectance, FloatTexture uRoughness,
                                 FloatTexture vRoughness, FloatTexture displacement, Image normalMap, bool remapRoughness,
                                 MicrofacetDistributionType distribution = MicrofacetDistributionType.TrowbridgeReitz, Float gamma = 2)
        {
            this.displacement = displacement; this.normalMap = normalMap; this.eta = eta; this.k = k;
            this.reflectance = reflectance; this.uRoughness = uRoughness; this.vRoughness = vRoughness;
            this.remapRoughness = remapRoughness;
            this.distribution = distribution;
            this.gamma = gamma;
        }

        public static string Name() { return "ConductorMaterial"; }

        public override FloatTexture GetDisplacement() { return displacement; }
        public override Image GetNormalMap() { return normalMap; }
        public override bool HasSubsurfaceScattering() { return false; }

        public override BSSRDF GetBSSRDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                           ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            return null;
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ ConductorMaterial displacement: %s normalMap: %s eta: %s " +
                                        "k: %s reflectance: %s uRoughness: %s vRoughness: %s " +
                                        "remapRoughness: %s ]",
                                        displacement, (normalMap != null ? normalMap.ToString() : "(nullptr)"), eta, k, reflectance, uRoughness,
                                        vRoughness, remapRoughness);
        }

        public static ConductorMaterial Create(TextureParameterDictionary parameters, Image normalMap, FileLoc loc, Allocator alloc)
        {
            SpectrumTexture eta = parameters.GetSpectrumTextureOrNull("eta", SpectrumType.Unbounded, alloc);
            SpectrumTexture k = parameters.GetSpectrumTextureOrNull("k", SpectrumType.Unbounded, alloc);
            SpectrumTexture reflectance = parameters.GetSpectrumTextureOrNull("reflectance", SpectrumType.Albedo, alloc);

            if (reflectance != null && (eta != null || k != null))
                pbrt.ErrorExit(loc, "For the conductor material, both \"reflectance\" " +
                                       "and \"eta\" and \"k\" can't be provided.");
            if (reflectance == null)
            {
                if (eta == null)
                    eta = new SpectrumConstantTexture(pbrt.GetNamedSpectrum("metal-Cu-eta"));
                if (k == null)
                    k = new SpectrumConstantTexture(pbrt.GetNamedSpectrum("metal-Cu-k"));
            }

            FloatTexture uRoughness = parameters.GetFloatTextureOrNull("uroughness", alloc);
            FloatTexture vRoughness = parameters.GetFloatTextureOrNull("vroughness", alloc);
            if (uRoughness == null)
                uRoughness = parameters.GetFloatTexture("roughness", 0f, alloc);
            if (vRoughness == null)
                vRoughness = parameters.GetFloatTexture("roughness", 0f, alloc);

            FloatTexture displacement = parameters.GetFloatTextureOrNull("displacement", alloc);
            bool remapRoughness = parameters.GetOneBool("remaproughness", true);
            // (not in pbrt-v4) "string distribution": "ggx" (the default), "beckmann" or "studentt", with "float gamma" for the last
            MicrofacetDistributionType distribution = MicrofacetDistribution.ParseType(parameters.GetOneString("distribution", "ggx"), loc);
            Float gamma = parameters.GetOneFloat("gamma", 2f);
            ConductorMaterial conductor = new ConductorMaterial(eta, k, reflectance, uRoughness, vRoughness, displacement, normalMap, remapRoughness,
                                                                distribution, gamma);
            conductor.retroreflectivity = Clamp(parameters.GetOneFloat("retroreflectivity", 0f), 0, 1);
            return conductor;
        }

        // ConductorMaterial Private Data
        private FloatTexture displacement;
        private Image normalMap;
        private SpectrumTexture eta, k, reflectance;
        private FloatTexture uRoughness, vRoughness;
        private bool remapRoughness;
        private MicrofacetDistributionType distribution;
        private Float gamma;
    }

    // CoatedDiffuseMaterial Definition
    public sealed unsafe class CoatedDiffuseMaterial : Material
    {
        // CoatedDiffuseMaterial Public Methods
        public CoatedDiffuseMaterial(SpectrumTexture reflectance, FloatTexture uRoughness, FloatTexture vRoughness,
                                     FloatTexture thickness, SpectrumTexture albedo, FloatTexture g, Spectrum eta,
                                     FloatTexture displacement, Image normalMap, bool remapRoughness, int maxDepth,
                                     int nSamples)
        {
            this.displacement = displacement; this.normalMap = normalMap; this.reflectance = reflectance;
            this.uRoughness = uRoughness; this.vRoughness = vRoughness; this.thickness = thickness;
            this.albedo = albedo; this.g = g; this.eta = eta; this.remapRoughness = remapRoughness;
            this.maxDepth = maxDepth; this.nSamples = nSamples;
        }

        public static string Name() { return "CoatedDiffuseMaterial"; }

        public override bool CanEvaluateTextures<TextureEvaluator>(TextureEvaluator texEval)
        {
            return texEval.CanEvaluate(new FloatTexture[] { uRoughness, vRoughness, thickness, g },
                                       new SpectrumTexture[] { reflectance, albedo });
        }

        // CoatedDiffuseMaterial Method Definitions
        public CoatedDiffuseBxDF GetBxDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                           ref SampledWavelengths lambda)
            where TextureEvaluator : ITextureEvaluator
        {
            // Initialize diffuse component of plastic material
            SampledSpectrum r = pbrt.Clamp(texEval.Apply(reflectance, ctx, lambda), 0, 1);

            // Create microfacet distribution _distrib_ for coated diffuse material
            Float urough = texEval.Apply(uRoughness, ctx);
            Float vrough = texEval.Apply(vRoughness, ctx);
            if (remapRoughness)
            {
                urough = TrowbridgeReitzDistribution.RoughnessToAlpha(urough);
                vrough = TrowbridgeReitzDistribution.RoughnessToAlpha(vrough);
            }
            TrowbridgeReitzDistribution distrib = new TrowbridgeReitzDistribution(urough, vrough);

            Float thick = texEval.Apply(thickness, ctx);

            // (not as in pbrt-v4) with "texture eta", the index of the texture at the point
            Float sampledEta = ParametricSpectra.SampleIndex(eta, etaTexture, texEval, ctx, ref lambda);

            SampledSpectrum a = pbrt.Clamp(texEval.Apply(albedo, ctx, lambda), 0, 1);
            Float gg = pbrt.Clamp(texEval.Apply(g, ctx), -1, 1);

            return new CoatedDiffuseBxDF(new DielectricBxDF(sampledEta, distrib), new DiffuseBxDF(r), thick, a, gg, maxDepth, nSamples);
        }

        public override BSDF GetBSDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                       ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            CoatedDiffuseBxDF bxdf = GetBxDF(texEval, ctx, ref lambda);
            return new BSDF(ctx.n, ctx.ns, ctx.dpdus, buf.Alloc(bxdf));
        }

        public override FloatTexture GetDisplacement() { return displacement; }
        public override Image GetNormalMap() { return normalMap; }

        public override BSSRDF GetBSSRDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                           ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            return null;
        }
        public override bool HasSubsurfaceScattering() { return false; }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ CoatedDiffuseMaterial displacement: %s normalMap: %s reflectance: %s " +
                                        "uRoughness: %s vRoughness: %s thickness: %s eta: %s remapRoughness: %s ]",
                                        displacement, (normalMap != null ? normalMap.ToString() : "(nullptr)"), reflectance, uRoughness, vRoughness,
                                        thickness, eta, remapRoughness);
        }

        public static CoatedDiffuseMaterial Create(TextureParameterDictionary parameters, Image normalMap, FileLoc loc, Allocator alloc)
        {
            SpectrumTexture reflectance = parameters.GetSpectrumTexture("reflectance", null, SpectrumType.Albedo, alloc);
            if (reflectance == null)
                reflectance = new SpectrumConstantTexture(new ConstantSpectrum(0.5f));

            FloatTexture uRoughness = parameters.GetFloatTextureOrNull("uroughness", alloc);
            FloatTexture vRoughness = parameters.GetFloatTextureOrNull("vroughness", alloc);
            if (uRoughness == null)
                uRoughness = parameters.GetFloatTexture("roughness", 0f, alloc);
            if (vRoughness == null)
                vRoughness = parameters.GetFloatTexture("roughness", 0f, alloc);

            FloatTexture thickness = parameters.GetFloatTexture("thickness", .01f, alloc);

            // (not as in pbrt-v4) The index, with the dispersion formulas of the C++ fork ("sellmeier", "cauchy", "cauchyb")
            Spectrum eta = ParametricSpectra.Index(parameters, "eta", alloc);
            // (not in pbrt-v4) "texture eta": an index that varies over the surface (a spectrum texture)
            SpectrumTexture etaTexture = ParametricSpectra.IndexTexture(parameters, "eta", eta, alloc);
            if (eta == null)
                eta = new ConstantSpectrum(1.5f);

            int maxDepth = parameters.GetOneInt("maxdepth", 10);
            int nSamples = parameters.GetOneInt("nsamples", 1);

            FloatTexture g = parameters.GetFloatTexture("g", 0f, alloc);
            SpectrumTexture albedo = parameters.GetSpectrumTexture("albedo", null, SpectrumType.Albedo, alloc);
            if (albedo == null)
                albedo = new SpectrumConstantTexture(new ConstantSpectrum(0f));

            FloatTexture displacement = parameters.GetFloatTextureOrNull("displacement", alloc);
            bool remapRoughness = parameters.GetOneBool("remaproughness", true);

            CoatedDiffuseMaterial coated = new CoatedDiffuseMaterial(reflectance, uRoughness, vRoughness, thickness, albedo, g, eta, displacement,
                                                                     normalMap, remapRoughness, maxDepth, nSamples);
            coated.etaTexture = etaTexture;
            return coated;
        }

        // (not in pbrt-v4) The index of the coat as a texture, null without "texture eta"
        public SpectrumTexture etaTexture = null;

        // CoatedDiffuseMaterial Private Members
        private FloatTexture displacement;
        private Image normalMap;
        private SpectrumTexture reflectance, albedo;
        private FloatTexture uRoughness, vRoughness, thickness, g;
        private Spectrum eta;
        private bool remapRoughness;
        private int maxDepth, nSamples;
    }

    // CoatedConductorMaterial Definition
    public sealed unsafe class CoatedConductorMaterial : Material
    {
        // CoatedConductorMaterial Public Methods
        public CoatedConductorMaterial(FloatTexture interfaceURoughness, FloatTexture interfaceVRoughness,
                                       FloatTexture thickness, Spectrum interfaceEta, FloatTexture g, SpectrumTexture albedo,
                                       FloatTexture conductorURoughness, FloatTexture conductorVRoughness,
                                       SpectrumTexture conductorEta, SpectrumTexture k, SpectrumTexture reflectance,
                                       FloatTexture displacement, Image normalMap, bool remapRoughness, int maxDepth,
                                       int nSamples)
        {
            this.displacement = displacement; this.normalMap = normalMap;
            this.interfaceURoughness = interfaceURoughness; this.interfaceVRoughness = interfaceVRoughness;
            this.thickness = thickness; this.interfaceEta = interfaceEta; this.albedo = albedo; this.g = g;
            this.conductorURoughness = conductorURoughness; this.conductorVRoughness = conductorVRoughness;
            this.conductorEta = conductorEta; this.k = k; this.reflectance = reflectance;
            this.remapRoughness = remapRoughness; this.maxDepth = maxDepth; this.nSamples = nSamples;
        }

        public static string Name() { return "CoatedConductorMaterial"; }

        public override bool CanEvaluateTextures<TextureEvaluator>(TextureEvaluator texEval)
        {
            return texEval.CanEvaluate(new FloatTexture[] { interfaceURoughness, interfaceVRoughness, thickness, g,
                                                            conductorURoughness, conductorVRoughness },
                                       new SpectrumTexture[] { conductorEta, k, reflectance, albedo });
        }

        public CoatedConductorBxDF GetBxDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                             ref SampledWavelengths lambda)
            where TextureEvaluator : ITextureEvaluator
        {
            Float iurough = texEval.Apply(interfaceURoughness, ctx);
            Float ivrough = texEval.Apply(interfaceVRoughness, ctx);
            if (remapRoughness)
            {
                iurough = TrowbridgeReitzDistribution.RoughnessToAlpha(iurough);
                ivrough = TrowbridgeReitzDistribution.RoughnessToAlpha(ivrough);
            }
            TrowbridgeReitzDistribution interfaceDistrib = new TrowbridgeReitzDistribution(iurough, ivrough);

            Float thick = texEval.Apply(thickness, ctx);

            // (not as in pbrt-v4) with "texture interface.eta", the index of the texture at the point
            Float ieta = ParametricSpectra.SampleIndex(interfaceEta, interfaceEtaTexture, texEval, ctx, ref lambda);

            SampledSpectrum ce, ck;
            if (conductorEta != null)
            {
                ce = texEval.Apply(conductorEta, ctx, lambda);
                ck = texEval.Apply(k, ctx, lambda);
            }
            else
            {
                // Avoid r==1 NaN case...
                SampledSpectrum r = pbrt.Clamp(texEval.Apply(reflectance, ctx, lambda), 0, .9999f);
                ce = new SampledSpectrum(1f);
                ck = 2 * pbrt.Sqrt(r) / pbrt.Sqrt(pbrt.ClampZero(new SampledSpectrum(1f) - r));
            }
            ce /= ieta;
            ck /= ieta;

            Float curough = texEval.Apply(conductorURoughness, ctx);
            Float cvrough = texEval.Apply(conductorVRoughness, ctx);
            if (remapRoughness)
            {
                curough = TrowbridgeReitzDistribution.RoughnessToAlpha(curough);
                cvrough = TrowbridgeReitzDistribution.RoughnessToAlpha(cvrough);
            }
            TrowbridgeReitzDistribution conductorDistrib = new TrowbridgeReitzDistribution(curough, cvrough);

            SampledSpectrum a = pbrt.Clamp(texEval.Apply(albedo, ctx, lambda), 0, 1);
            Float gg = pbrt.Clamp(texEval.Apply(g, ctx), -1, 1);

            return new CoatedConductorBxDF(new DielectricBxDF(ieta, interfaceDistrib),
                                           new ConductorBxDF(conductorDistrib, ce, ck), thick, a, gg, maxDepth, nSamples);
        }

        public override BSDF GetBSDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                       ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            CoatedConductorBxDF bxdf = GetBxDF(texEval, ctx, ref lambda);
            return new BSDF(ctx.n, ctx.ns, ctx.dpdus, buf.Alloc(bxdf));
        }

        public override FloatTexture GetDisplacement() { return displacement; }
        public override Image GetNormalMap() { return normalMap; }

        public override BSSRDF GetBSSRDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                           ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            return null;
        }
        public override bool HasSubsurfaceScattering() { return false; }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ CoatedConductorMaterial displacement: %s normalMap: %s " +
                                        "interfaceURoughness: %s interfaceVRoughness: %s thickness: %s " +
                                        "interfaceEta: %s g: %s albedo: %s conductorURoughness: %s " +
                                        "conductorVRoughness: %s conductorEta: %s k: %s " +
                                        "conductorReflectance: %s remapRoughness: %s maxDepth: %d nSamples: %d ]",
                                        displacement, (normalMap != null ? normalMap.ToString() : "(nullptr)"), interfaceURoughness,
                                        interfaceVRoughness, thickness, interfaceEta, g, albedo, conductorURoughness,
                                        conductorVRoughness, conductorEta, k, reflectance, remapRoughness, maxDepth, nSamples);
        }

        public static CoatedConductorMaterial Create(TextureParameterDictionary parameters, Image normalMap, FileLoc loc,
                                                     Allocator alloc)
        {
            // interface
            FloatTexture interfaceURoughness = parameters.GetFloatTextureOrNull("interface.uroughness", alloc);
            FloatTexture interfaceVRoughness = parameters.GetFloatTextureOrNull("interface.vroughness", alloc);
            if (interfaceURoughness == null)
                interfaceURoughness = parameters.GetFloatTexture("interface.roughness", 0f, alloc);
            if (interfaceVRoughness == null)
                interfaceVRoughness = parameters.GetFloatTexture("interface.roughness", 0f, alloc);

            FloatTexture thickness = parameters.GetFloatTexture("thickness", .01f, alloc);

            // (not as in pbrt-v4) The index, with the dispersion formulas of the C++ fork ("interface.sellmeier", "interface.cauchy",
            // "interface.cauchyb"), or "texture interface.eta": an index that varies over the surface
            Spectrum interfaceEta = ParametricSpectra.Index(parameters, "interface.eta", alloc);
            SpectrumTexture interfaceEtaTexture = ParametricSpectra.IndexTexture(parameters, "interface.eta", interfaceEta, alloc);
            if (interfaceEta == null)
                interfaceEta = new ConstantSpectrum(1.5f);

            // conductor
            FloatTexture conductorURoughness = parameters.GetFloatTextureOrNull("conductor.uroughness", alloc);
            FloatTexture conductorVRoughness = parameters.GetFloatTextureOrNull("conductor.vroughness", alloc);
            if (conductorURoughness == null)
                conductorURoughness = parameters.GetFloatTexture("conductor.roughness", 0f, alloc);
            if (conductorVRoughness == null)
                conductorVRoughness = parameters.GetFloatTexture("conductor.roughness", 0f, alloc);
            SpectrumTexture conductorEta = parameters.GetSpectrumTextureOrNull("conductor.eta", SpectrumType.Unbounded, alloc);
            SpectrumTexture k = parameters.GetSpectrumTextureOrNull("conductor.k", SpectrumType.Unbounded, alloc);
            SpectrumTexture reflectance = parameters.GetSpectrumTextureOrNull("reflectance", SpectrumType.Albedo, alloc);

            if (reflectance != null && (conductorEta != null || k != null))
                pbrt.ErrorExit(loc, "For the coated conductor material, both \"reflectance\" " +
                                       "and \"eta\" and \"k\" can't be provided.");
            if (reflectance == null)
            {
                if (conductorEta == null)
                    conductorEta = new SpectrumConstantTexture(pbrt.GetNamedSpectrum("metal-Cu-eta"));
                if (k == null)
                    k = new SpectrumConstantTexture(pbrt.GetNamedSpectrum("metal-Cu-k"));
            }

            int maxDepth = parameters.GetOneInt("maxdepth", 10);
            int nSamples = parameters.GetOneInt("nsamples", 1);

            FloatTexture g = parameters.GetFloatTexture("g", 0f, alloc);
            SpectrumTexture albedo = parameters.GetSpectrumTexture("albedo", null, SpectrumType.Albedo, alloc);
            if (albedo == null)
                albedo = new SpectrumConstantTexture(new ConstantSpectrum(0f));

            FloatTexture displacement = parameters.GetFloatTextureOrNull("displacement", alloc);
            bool remapRoughness = parameters.GetOneBool("remaproughness", true);

            CoatedConductorMaterial coated = new CoatedConductorMaterial(interfaceURoughness, interfaceVRoughness, thickness, interfaceEta, g, albedo,
                                                                         conductorURoughness, conductorVRoughness, conductorEta, k, reflectance,
                                                                         displacement, normalMap, remapRoughness, maxDepth, nSamples);
            coated.interfaceEtaTexture = interfaceEtaTexture;
            return coated;
        }

        // CoatedConductorMaterial Private Members
        private FloatTexture displacement;
        private Image normalMap;
        private FloatTexture interfaceURoughness, interfaceVRoughness, thickness;
        private Spectrum interfaceEta;
        // (not in pbrt-v4) The index of the interface as a texture, null without "texture interface.eta"
        public SpectrumTexture interfaceEtaTexture = null;
        private FloatTexture g;
        private SpectrumTexture albedo;
        private FloatTexture conductorURoughness, conductorVRoughness;
        private SpectrumTexture conductorEta, k, reflectance;
        private bool remapRoughness;
        private int maxDepth, nSamples;
    }

    // SubsurfaceMaterial Definition
    public sealed unsafe class SubsurfaceMaterial : Material
    {
        // SubsurfaceMaterial Public Methods
        public SubsurfaceMaterial(Float scale, SpectrumTexture sigma_a, SpectrumTexture sigma_s, SpectrumTexture reflectance,
                                  SpectrumTexture mfp, Float g, Float eta, FloatTexture uRoughness, FloatTexture vRoughness,
                                  FloatTexture displacement, Image normalMap, bool remapRoughness, Allocator alloc)
        {
            this.displacement = displacement; this.normalMap = normalMap; this.scale = scale; this.sigma_a = sigma_a;
            this.sigma_s = sigma_s; this.reflectance = reflectance; this.mfp = mfp; this.uRoughness = uRoughness;
            this.vRoughness = vRoughness; this.eta = eta; this.remapRoughness = remapRoughness;
            table = new BSSRDFTable(100, 64, alloc);
            pbrt.ComputeBeamDiffusionBSSRDF(g, eta, table);
        }

        public static string Name() { return "SubsurfaceMaterial"; }

        public override bool CanEvaluateTextures<TextureEvaluator>(TextureEvaluator texEval)
        {
            return texEval.CanEvaluate(new FloatTexture[] { uRoughness, vRoughness }, new SpectrumTexture[] { sigma_a, sigma_s });
        }

        public DielectricBxDF GetBxDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                        ref SampledWavelengths lambda)
            where TextureEvaluator : ITextureEvaluator
        {
            // Initialize BSDF for _SubsurfaceMaterial_
            Float urough = texEval.Apply(uRoughness, ctx), vrough = texEval.Apply(vRoughness, ctx);
            if (remapRoughness)
            {
                urough = TrowbridgeReitzDistribution.RoughnessToAlpha(urough);
                vrough = TrowbridgeReitzDistribution.RoughnessToAlpha(vrough);
            }
            TrowbridgeReitzDistribution distrib = new TrowbridgeReitzDistribution(urough, vrough);
            // Initialize _bsdf_ for smooth or rough dielectric
            return new DielectricBxDF(eta, distrib);
        }

        public override BSDF GetBSDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                       ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            DielectricBxDF bxdf = GetBxDF(texEval, ctx, ref lambda);
            return new BSDF(ctx.n, ctx.ns, ctx.dpdus, buf.Alloc(bxdf));
        }

        public TabulatedBSSRDF GetTabulatedBSSRDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                                    ref SampledWavelengths lambda)
            where TextureEvaluator : ITextureEvaluator
        {
            SampledSpectrum sig_a = new SampledSpectrum(), sig_s = new SampledSpectrum();
            if (sigma_a != null && sigma_s != null)
            {
                // Evaluate textures for $\sigma_\roman{a}$ and $\sigma_\roman{s}$
                sig_a = pbrt.ClampZero(scale * texEval.Apply(sigma_a, ctx, lambda));
                sig_s = pbrt.ClampZero(scale * texEval.Apply(sigma_s, ctx, lambda));
            }
            else
            {
                // Compute _sig_a_ and _sig_s_ from reflectance and mfp
                pbrt.DCHECK(reflectance != null && mfp != null);
                SampledSpectrum mfree = pbrt.ClampZero(scale * texEval.Apply(mfp, ctx, lambda));
                SampledSpectrum r = pbrt.Clamp(texEval.Apply(reflectance, ctx, lambda), 0, 1);
                pbrt.SubsurfaceFromDiffuse(table, r, mfree, &sig_a, &sig_s);
            }
            return new TabulatedBSSRDF(ctx.p, ctx.ns, ctx.wo, eta, sig_a, sig_s, table);
        }

        public override BSSRDF GetBSSRDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                           ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            TabulatedBSSRDF bssrdf = GetTabulatedBSSRDF(texEval, ctx, ref lambda);
            return buf.Alloc(bssrdf);
        }

        public override FloatTexture GetDisplacement() { return displacement; }
        public override Image GetNormalMap() { return normalMap; }

        public override bool HasSubsurfaceScattering() { return true; }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ SubsurfaceMaterial displacement: %s normalMap: %s scale: %f " +
                                        "sigma_a: %s sigma_s: %s reflectance: %s mfp: %s uRoughness: %s " +
                                        "vRoughness: %s scale: %f eta: %f remapRoughness: %s ]",
                                        displacement, (normalMap != null ? normalMap.ToString() : "(nullptr)"), scale, sigma_a, sigma_s, reflectance,
                                        mfp, uRoughness, vRoughness, scale, eta, remapRoughness);
        }

        public static SubsurfaceMaterial Create(TextureParameterDictionary parameters, Image normalMap, FileLoc loc, Allocator alloc)
        {
            SpectrumTexture sigma_a = null, sigma_s = null, reflectance = null, mfp = null;

            Float g = parameters.GetOneFloat("g", 0.0f);

            // 4, mutually-exclusive, ways to specify the subsurface properties...
            string name = parameters.GetOneString("name", "");
            if (name.Length != 0)
            {
                // 1. By name
                Spectrum sig_a = null, sig_s = null;
                if (!pbrt.GetMediumScatteringProperties(name, ref sig_a, ref sig_s, alloc))
                    pbrt.ErrorExit(loc, pbrt.StringPrintf("%s: named medium not found.", name));
                if (g != 0)
                    pbrt.Warning(loc, "Non-zero \"g\" ignored with named scattering coefficients.");
                g = 0; /* Enforce g=0 (the database specifies reduced scattering
                          coefficients) */
                sigma_a = new SpectrumConstantTexture(sig_a);
                sigma_s = new SpectrumConstantTexture(sig_s);
            }
            else
            {
                // 2. sigma_a and sigma_s directly specified
                sigma_a = parameters.GetSpectrumTextureOrNull("sigma_a", SpectrumType.Unbounded, alloc);
                sigma_s = parameters.GetSpectrumTextureOrNull("sigma_s", SpectrumType.Unbounded, alloc);
                if (sigma_a != null && sigma_s == null)
                    pbrt.ErrorExit(loc, "Provided \"sigma_a\" parameter without \"sigma_s\".");
                if (sigma_s != null && sigma_a == null)
                    pbrt.ErrorExit(loc, "Provided \"sigma_s\" parameter without \"sigma_a\".");

                if (sigma_a == null && sigma_s == null)
                {
                    // 3. RGB/Spectrum, reflectance
                    reflectance = parameters.GetSpectrumTextureOrNull("reflectance", SpectrumType.Albedo, alloc);
                    if (reflectance != null)
                    {
                        Spectrum one = new ConstantSpectrum(1f);
                        mfp = parameters.GetSpectrumTexture("mfp", one, SpectrumType.Unbounded, alloc);
                    }
                    else
                    {
                        // 4. nothing specified -- use defaults
                        RGBUnboundedSpectrum defaultSigma_a = new RGBUnboundedSpectrum(RGBColorSpace.sRGB, new RGB(.0011f, .0024f, .014f));
                        RGBUnboundedSpectrum defaultSigma_s = new RGBUnboundedSpectrum(RGBColorSpace.sRGB, new RGB(2.55f, 3.21f, 3.77f));
                        sigma_a = new SpectrumConstantTexture(defaultSigma_a);
                        sigma_s = new SpectrumConstantTexture(defaultSigma_s);
                    }
                }
            }

            Float scale = parameters.GetOneFloat("scale", 1f);
            Float eta = parameters.GetOneFloat("eta", 1.33f);

            FloatTexture uRoughness = parameters.GetFloatTextureOrNull("uroughness", alloc);
            FloatTexture vRoughness = parameters.GetFloatTextureOrNull("vroughness", alloc);
            if (uRoughness == null)
                uRoughness = parameters.GetFloatTexture("roughness", 0f, alloc);
            if (vRoughness == null)
                vRoughness = parameters.GetFloatTexture("roughness", 0f, alloc);

            FloatTexture displacement = parameters.GetFloatTextureOrNull("displacement", alloc);
            bool remapRoughness = parameters.GetOneBool("remaproughness", true);
            return new SubsurfaceMaterial(scale, sigma_a, sigma_s, reflectance, mfp, g, eta, uRoughness, vRoughness,
                                          displacement, normalMap, remapRoughness, alloc);
        }

        // SubsurfaceMaterial Private Members
        private FloatTexture displacement;
        private Image normalMap;
        private SpectrumTexture sigma_a, sigma_s, reflectance, mfp;
        private Float scale, eta;
        private FloatTexture uRoughness, vRoughness;
        private bool remapRoughness;
        private BSSRDFTable table;
    }

    // DiffuseTransmissionMaterial Definition
    public sealed unsafe class DiffuseTransmissionMaterial : Material
    {
        // DiffuseTransmissionMaterial Public Methods
        public DiffuseTransmissionMaterial(SpectrumTexture reflectance, SpectrumTexture transmittance,
                                           FloatTexture displacement, Image normalMap, Float scale)
        {
            this.displacement = displacement; this.normalMap = normalMap; this.reflectance = reflectance;
            this.transmittance = transmittance; this.scale = scale;
        }

        public static string Name() { return "DiffuseTransmissionMaterial"; }

        public override bool CanEvaluateTextures<TextureEvaluator>(TextureEvaluator texEval)
        {
            return texEval.CanEvaluate(new FloatTexture[0], new SpectrumTexture[] { reflectance, transmittance });
        }

        public DiffuseTransmissionBxDF GetBxDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                                 ref SampledWavelengths lambda)
            where TextureEvaluator : ITextureEvaluator
        {
            SampledSpectrum r = pbrt.Clamp(scale * texEval.Apply(reflectance, ctx, lambda), 0, 1);
            SampledSpectrum t = pbrt.Clamp(scale * texEval.Apply(transmittance, ctx, lambda), 0, 1);
            return new DiffuseTransmissionBxDF(r, t);
        }

        public override BSDF GetBSDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                       ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            DiffuseTransmissionBxDF bxdf = GetBxDF(texEval, ctx, ref lambda);
            return new BSDF(ctx.n, ctx.ns, ctx.dpdus, buf.Alloc(bxdf));
        }

        public override FloatTexture GetDisplacement() { return displacement; }
        public override Image GetNormalMap() { return normalMap; }
        public override bool HasSubsurfaceScattering() { return false; }

        public override BSSRDF GetBSSRDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                           ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            return null;
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ DiffuseTransmissionMaterial displacement: %s reflectance: %s " +
                                        "transmittance: %s scale: %f ]", displacement, reflectance, transmittance, scale);
        }

        public static DiffuseTransmissionMaterial Create(TextureParameterDictionary parameters, Image normalMap, FileLoc loc,
                                                         Allocator alloc)
        {
            SpectrumTexture reflectance = parameters.GetSpectrumTexture("reflectance", null, SpectrumType.Albedo, alloc);
            if (reflectance == null)
                reflectance = new SpectrumConstantTexture(new ConstantSpectrum(0.25f));

            SpectrumTexture transmittance = parameters.GetSpectrumTexture("transmittance", null, SpectrumType.Albedo, alloc);
            if (transmittance == null)
                transmittance = new SpectrumConstantTexture(new ConstantSpectrum(0.25f));

            FloatTexture displacement = parameters.GetFloatTextureOrNull("displacement", alloc);
            Float scale = parameters.GetOneFloat("scale", 1f);

            return new DiffuseTransmissionMaterial(reflectance, transmittance, displacement, normalMap, scale);
        }

        // DiffuseTransmissionMaterial Private Data
        private FloatTexture displacement;
        private Image normalMap;
        private SpectrumTexture reflectance, transmittance;
        private Float scale;
    }

    // MeasuredMaterial Definition
    public sealed unsafe class MeasuredMaterial : Material
    {
        // MeasuredMaterial Public Methods
        public MeasuredBxDF GetBxDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx, ref SampledWavelengths lambda)
            where TextureEvaluator : ITextureEvaluator
        {
            return new MeasuredBxDF(brdf, lambda);
        }

        public override BSDF GetBSDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                       ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            MeasuredBxDF bxdf = GetBxDF(texEval, ctx, ref lambda);
            return new BSDF(ctx.n, ctx.ns, ctx.dpdus, buf.Alloc(bxdf));
        }

        public override FloatTexture GetDisplacement() { return displacement; }

        public static string Name() { return "MeasuredMaterial"; }

        public override bool CanEvaluateTextures<TextureEvaluator>(TextureEvaluator texEval) { return true; }
        public override bool HasSubsurfaceScattering() { return false; }
        public override Image GetNormalMap() { return normalMap; }

        public MeasuredMaterial(string filename, FloatTexture displacement, Image normalMap, Allocator alloc)
        {
            this.displacement = displacement;
            this.normalMap = normalMap;
            brdf = MeasuredBxDF.BRDFDataFromFile(filename, alloc);
        }

        public override BSSRDF GetBSSRDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                           ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            return null;
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ MeasuredMaterial displacement: %s normalMap: %s ]", displacement,
                                        (normalMap != null ? normalMap.ToString() : "(nullptr)"));
        }

        public static MeasuredMaterial Create(TextureParameterDictionary parameters, Image normalMap, FileLoc loc, Allocator alloc)
        {
            string filename = pbrt.ResolveFilename(parameters.GetOneString("filename", ""));
            if (filename.Length == 0)
            {
                pbrt.Error("Filename must be provided for MeasuredMaterial");
                return null;
            }
            FloatTexture displacement = parameters.GetFloatTextureOrNull("displacement", alloc);
            return new MeasuredMaterial(filename, displacement, normalMap, alloc);
        }

        // MeasuredMaterial Private Members
        private FloatTexture displacement;
        private Image normalMap;
        private MeasuredBxDFData brdf;
    }

    // Material factory (materials.cpp)
    public abstract unsafe partial class Material
    {
        // (not in pbrt-v4) The volume that the material owns (Fong et al., Production Volume Rendering, listing 1): the shapes
        // that use the material are the boundary of the medium named by "string volume", with the priority "integer
        // volumepriority". Crossing into such a shape enters the volume and crossing out of it leaves it.
        public void SetVolume(string name, int priority, bool canOverlap, int overlapPriority, FloatTexture volumeScale)
        {
            this.volumeScale = volumeScale;
            volumeName = name;
            volumePriority = priority;
            this.canOverlap = canOverlap;
            this.overlapPriority = overlapPriority;
        }

        public bool HasVolume() { return volumeName.Length != 0; }

        public int VolumePriority() { return volumePriority; }

        // Fong et al., section 3.9: whether the volume adds up with the volumes it overlaps ("bool overlap"; a volume that does
        // not, like the absorbing medium of a glass object that smoke cannot enter, hides them), and the rank of its integrator
        // ("integer overlappriority"; the volume that integrates the combination is the one of the highest rank)
        public bool CanOverlap() { return HasVolume() && canOverlap; }

        public int OverlapPriority() { return overlapPriority; }

        // The volume of the material, found by name among the media of the scene
        public Medium ResolveVolume(Func<string, FileLoc, Medium> findMedium, FileLoc loc)
        {
            volume = HasVolume() ? findMedium(volumeName, loc) : null;
            return volume;
        }

        // Fong et al., listing 1, Material::CreateVolume(ShadingContext), named GetVolume here after pbrt-v4's GetBSDF: a material with a "volumescale" texture creates the
        // volume for the point where the path enters it, the medium with its coefficients scaled by the texture there. Any
        // other material has the one volume of the scene.
        public bool CreatesVolumePerEntry() { return HasVolume() && volumeScale != null; }

        public Medium GetVolume(Interaction ctx)
        {
            if (volumeScale == null)
                return volume;
            return new ScaledMedium(volume, Math.Max(0, volumeScale.Evaluate(new TextureEvalContext(ctx))));
        }

        string volumeName = "";
        int volumePriority;
        bool canOverlap;
        int overlapPriority;
        Medium volume;
        FloatTexture volumeScale;

        public static Material Create(string name, TextureParameterDictionary parameters, Image normalMap,
                                      SortedDictionary<string, Material> namedMaterials, FileLoc loc, Allocator alloc)
        {
            Material material = null;
            if (name.Length == 0 || name == "none")
            {
                pbrt.Warning(loc, pbrt.StringPrintf("Material \"%s\" is deprecated; use \"interface\" instead.", name));
                return new InterfaceMaterial();
            }
            else if (name == "interface")
                return new InterfaceMaterial();
            else if (name == "osl")
                material = OSLMaterial.Create(parameters, normalMap, loc, alloc);
            else if (name == "diffuse")
                material = DiffuseMaterial.Create(parameters, normalMap, loc, alloc);
            else if (name == "hazygloss")
                material = HazyGlossMaterial.Create(parameters, normalMap, loc, alloc);
            else if (name == "sggxsheen")
                material = SheenMaterial.Create(parameters, normalMap, loc, alloc);
            else if (name == "multiscatterconductor")
                material = MicrosurfaceMaterial.Create(MicrosurfaceKind.MultiScatterConductor, parameters, normalMap, loc, alloc);
            else if (name == "multiscatterdielectric")
                material = MicrosurfaceMaterial.Create(MicrosurfaceKind.MultiScatterDielectric, parameters, normalMap, loc, alloc);
            else if (name == "multiscatterdiffuse")
                material = MicrosurfaceMaterial.Create(MicrosurfaceKind.MultiScatterDiffuse, parameters, normalMap, loc, alloc);
            else if (name == "pearlescent")
                material = PearlescentMaterial.Create(parameters, normalMap, loc, alloc);
            else if (name == "eondiffuse")
                material = EonDiffuseMaterial.Create(parameters, normalMap, loc, alloc);
            else if (name == "vmfdiffuse")
                material = VmfDiffuseMaterial.Create(parameters, normalMap, loc, alloc);
            else if (name == "coateddiffuse")
                material = CoatedDiffuseMaterial.Create(parameters, normalMap, loc, alloc);
            else if (name == "coatedconductor")
                material = CoatedConductorMaterial.Create(parameters, normalMap, loc, alloc);
            else if (name == "diffusetransmission")
                material = DiffuseTransmissionMaterial.Create(parameters, normalMap, loc, alloc);
            else if (name == "dielectric")
                material = DielectricMaterial.Create(parameters, normalMap, loc, alloc);
            else if (name == "thincoat")
                material = ThinCoatMaterial.Create(parameters, normalMap, loc, alloc);
            else if (name == "thinfilm")
                material = ThinFilmMaterial.Create(parameters, normalMap, loc, alloc);
            else if (name == "thindielectric")
                material = ThinDielectricMaterial.Create(parameters, normalMap, loc, alloc);
            else if (name == "hair")
                material = HairMaterial.Create(parameters, loc, alloc);
            else if (name == "conductor")
                material = ConductorMaterial.Create(parameters, normalMap, loc, alloc);
            else if (name == "measured")
                material = MeasuredMaterial.Create(parameters, normalMap, loc, alloc);
            else if (name == "subsurface")
                material = SubsurfaceMaterial.Create(parameters, normalMap, loc, alloc);
            else if (name == "mix")
            {
                List<string> materialNames = parameters.GetStringArray("materials");
                if (materialNames.Count != 2)
                    pbrt.ErrorExit("Must provide two values for \"string materials\" for mix material.");

                Material[] materials = new Material[2];
                for (int i = 0; i < 2; ++i)
                {
                    if (!namedMaterials.TryGetValue(materialNames[i], out Material m))
                        pbrt.ErrorExit(pbrt.StringPrintf("%s: named material not found.", materialNames[i]));
                    materials[i] = m;

                    if (materials[i] == null)
                        pbrt.ErrorExit(pbrt.StringPrintf("%s: an \"interface\" material cannot be used as an element of " +
                                                               "the \"mix\" material.", materialNames[i]));
                }

                material = MixMaterial.Create(materials, parameters, loc, alloc);
            }
            else
                pbrt.ErrorExit(loc, pbrt.StringPrintf("%s: material type unknown.", name));

            if (material == null)
                pbrt.ErrorExit(loc, pbrt.StringPrintf("%s: unable to create material.", name));

            parameters.ReportUnused();
            ++pbrt.nMaterialsCreated;
            return material;
        }
    }

}  // namespace pbrt
