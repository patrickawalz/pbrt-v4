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
            // Compute offset positions and evaluate displacement texture
            TextureEvalContext shiftedCtx = ctx;

            // Shift _shiftedCtx_ _du_ in the $u$ direction
            Float du = .5f * (std.abs(ctx.dudx) + std.abs(ctx.dudy));
            if (du == 0)
                du = .0005f;
            shiftedCtx.p = ctx.p + du * ctx.shading.dpdu;
            shiftedCtx.uv = ctx.uv + new Vector2f(du, 0f);

            Float uDisplace = texEval.Apply(displacement, shiftedCtx);

            // Shift _shiftedCtx_ _dv_ in the $v$ direction
            Float dv = .5f * (std.abs(ctx.dvdx) + std.abs(ctx.dvdy));
            if (dv == 0)
                dv = .0005f;
            shiftedCtx.p = ctx.p + dv * ctx.shading.dpdv;
            shiftedCtx.uv = ctx.uv + new Vector2f(0f, dv);

            Float vDisplace = texEval.Apply(displacement, shiftedCtx);
            Float displace = texEval.Apply(displacement, ctx);

            // Compute bump-mapped differential geometry
            *dpdu = ctx.shading.dpdu + (uDisplace - displace) / du * (Vector3f)ctx.shading.n +
                    displace * (Vector3f)ctx.shading.dndu;
            *dpdv = ctx.shading.dpdv + (vDisplace - displace) / dv * (Vector3f)ctx.shading.n +
                    displace * (Vector3f)ctx.shading.dndv;
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
            dudx = t.dudx; dudy = t.dudy; dvdx = t.dvdx; dvdy = t.dvdy; faceIndex = t.faceIndex;
            wo = si.wo;
            ns = si.shading.n;
            dpdus = si.shading.dpdu;
        }

        public static implicit operator MaterialEvalContext(SurfaceInteraction si) { return new MaterialEvalContext(si); }

        public static implicit operator TextureEvalContext(MaterialEvalContext c)
        {
            return new TextureEvalContext(c.p, c.dpdx, c.dpdy, c.n, c.uv, c.dudx, c.dudy, c.dvdx, c.dvdy, c.faceIndex);
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
        // MaterialEvalContext members
        public Vector3f wo;
        public Normal3f ns;
        public Vector3f dpdus;
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
            shading = new ShadingGeometry();
            shading.n = si.shading.n;
            shading.dpdu = si.shading.dpdu;
            shading.dpdv = si.shading.dpdv;
            shading.dndu = si.shading.dndu;
            shading.dndv = si.shading.dndv;
        }

        public static implicit operator TextureEvalContext(NormalBumpEvalContext c)
        {
            return new TextureEvalContext(c.p, c.dpdx, c.dpdy, c.n, c.uv, c.dudx, c.dudy, c.dvdx, c.dvdy, c.faceIndex);
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
    }

    // DielectricMaterial Definition
    public sealed unsafe class DielectricMaterial : Material
    {
        // DielectricMaterial Public Methods
        public DielectricMaterial(FloatTexture uRoughness, FloatTexture vRoughness, Spectrum eta, FloatTexture displacement,
                                  Image normalMap, bool remapRoughness)
        {
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
            Spectrum eta;
            if (parameters.GetFloatArray("eta").Count != 0)
                eta = new ConstantSpectrum(parameters.GetFloatArray("eta")[0]);
            else
                eta = parameters.GetOneSpectrum("eta", null, SpectrumType.Unbounded, alloc);
            if (eta == null)
                eta = new ConstantSpectrum(1.5f);

            FloatTexture uRoughness = parameters.GetFloatTextureOrNull("uroughness", alloc);
            FloatTexture vRoughness = parameters.GetFloatTextureOrNull("vroughness", alloc);
            if (uRoughness == null)
                uRoughness = parameters.GetFloatTexture("roughness", 0f, alloc);
            if (vRoughness == null)
                vRoughness = parameters.GetFloatTexture("roughness", 0f, alloc);

            FloatTexture displacement = parameters.GetFloatTextureOrNull("displacement", alloc);
            bool remapRoughness = parameters.GetOneBool("remaproughness", true);
            return new DielectricMaterial(uRoughness, vRoughness, eta, displacement, normalMap, remapRoughness);
        }

        public override BSDF GetBSDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                       ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            DielectricBxDF bxdf = GetBxDF(texEval, ctx, ref lambda);
            return new BSDF(ctx.ns, ctx.dpdus, buf.Alloc(bxdf));
        }

        public DielectricBxDF GetBxDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                        ref SampledWavelengths lambda)
            where TextureEvaluator : ITextureEvaluator
        {
            // Compute index of refraction for dielectric material
            Float sampledEta = eta.Evaluate(lambda[0]);
            if (!(eta is ConstantSpectrum))
                lambda.TerminateSecondary();
            // Handle edge case in case lambda[0] is beyond the wavelengths stored by the
            // Spectrum.
            if (sampledEta == 0)
                sampledEta = 1;

            // Create microfacet distribution for dielectric material
            Float urough = texEval.Apply(uRoughness, ctx), vrough = texEval.Apply(vRoughness, ctx);
            if (remapRoughness)
            {
                urough = TrowbridgeReitzDistribution.RoughnessToAlpha(urough);
                vrough = TrowbridgeReitzDistribution.RoughnessToAlpha(vrough);
            }
            TrowbridgeReitzDistribution distrib = new TrowbridgeReitzDistribution(urough, vrough);

            // Return BSDF for dielectric material
            return new DielectricBxDF(sampledEta, distrib);
        }

        // DielectricMaterial Private Members
        private Image normalMap;
        private FloatTexture displacement;
        private FloatTexture uRoughness, vRoughness;
        private bool remapRoughness;
        private Spectrum eta;
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
            Float sampledEta = eta.Evaluate(lambda[0]);
            if (!(eta is ConstantSpectrum))
                lambda.TerminateSecondary();
            // Handle edge case in case lambda[0] is beyond the wavelengths stored by the
            // Spectrum.
            if (sampledEta == 0)
                sampledEta = 1;

            // Return BxDF for _ThinDielectricMaterial_
            return new ThinDielectricBxDF(sampledEta);
        }

        public override BSDF GetBSDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                       ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            ThinDielectricBxDF bxdf = GetBxDF(texEval, ctx, ref lambda);
            return new BSDF(ctx.ns, ctx.dpdus, buf.Alloc(bxdf));
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
            Spectrum eta;
            if (parameters.GetFloatArray("eta").Count != 0)
                eta = new ConstantSpectrum(parameters.GetFloatArray("eta")[0]);
            else
                eta = parameters.GetOneSpectrum("eta", null, SpectrumType.Unbounded, alloc);
            if (eta == null)
                eta = new ConstantSpectrum(1.5f);

            FloatTexture displacement = parameters.GetFloatTextureOrNull("displacement", alloc);

            return new ThinDielectricMaterial(eta, displacement, normalMap);
        }

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
            return new BSDF(ctx.ns, ctx.dpdus, buf.Alloc(bxdf));
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
            return new BSDF(ctx.ns, ctx.dpdus, buf.Alloc(bxdf));
        }

        // DiffuseMaterial Private Members
        private Image normalMap;
        private FloatTexture displacement;
        private SpectrumTexture reflectance;
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
            TrowbridgeReitzDistribution distrib = new TrowbridgeReitzDistribution(uRough, vRough);
            return new ConductorBxDF(distrib, etas, ks);
        }

        public override BSDF GetBSDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                       ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            ConductorBxDF bxdf = GetBxDF(texEval, ctx, ref lambda);
            return new BSDF(ctx.ns, ctx.dpdus, buf.Alloc(bxdf));
        }

        public ConductorMaterial(SpectrumTexture eta, SpectrumTexture k, SpectrumTexture reflectance, FloatTexture uRoughness,
                                 FloatTexture vRoughness, FloatTexture displacement, Image normalMap, bool remapRoughness)
        {
            this.displacement = displacement; this.normalMap = normalMap; this.eta = eta; this.k = k;
            this.reflectance = reflectance; this.uRoughness = uRoughness; this.vRoughness = vRoughness;
            this.remapRoughness = remapRoughness;
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
            return new ConductorMaterial(eta, k, reflectance, uRoughness, vRoughness, displacement, normalMap, remapRoughness);
        }

        // ConductorMaterial Private Data
        private FloatTexture displacement;
        private Image normalMap;
        private SpectrumTexture eta, k, reflectance;
        private FloatTexture uRoughness, vRoughness;
        private bool remapRoughness;
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

            Float sampledEta = eta.Evaluate(lambda[0]);
            if (!(eta is ConstantSpectrum))
                lambda.TerminateSecondary();
            if (sampledEta == 0)
                sampledEta = 1;

            SampledSpectrum a = pbrt.Clamp(texEval.Apply(albedo, ctx, lambda), 0, 1);
            Float gg = pbrt.Clamp(texEval.Apply(g, ctx), -1, 1);

            return new CoatedDiffuseBxDF(new DielectricBxDF(sampledEta, distrib), new DiffuseBxDF(r), thick, a, gg, maxDepth, nSamples);
        }

        public override BSDF GetBSDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                       ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            CoatedDiffuseBxDF bxdf = GetBxDF(texEval, ctx, ref lambda);
            return new BSDF(ctx.ns, ctx.dpdus, buf.Alloc(bxdf));
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

            Spectrum eta;
            if (parameters.GetFloatArray("eta").Count != 0)
                eta = new ConstantSpectrum(parameters.GetFloatArray("eta")[0]);
            else
                eta = parameters.GetOneSpectrum("eta", null, SpectrumType.Unbounded, alloc);
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

            return new CoatedDiffuseMaterial(reflectance, uRoughness, vRoughness, thickness, albedo, g, eta, displacement,
                                             normalMap, remapRoughness, maxDepth, nSamples);
        }

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

            Float ieta = interfaceEta.Evaluate(lambda[0]);
            if (!(interfaceEta is ConstantSpectrum))
                lambda.TerminateSecondary();
            if (ieta == 0)
                ieta = 1;

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
            return new BSDF(ctx.ns, ctx.dpdus, buf.Alloc(bxdf));
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

            Spectrum interfaceEta;
            if (parameters.GetFloatArray("interface.eta").Count != 0)
                interfaceEta = new ConstantSpectrum(parameters.GetFloatArray("interface.eta")[0]);
            else
                interfaceEta = parameters.GetOneSpectrum("interface.eta", null, SpectrumType.Unbounded, alloc);
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

            return new CoatedConductorMaterial(interfaceURoughness, interfaceVRoughness, thickness, interfaceEta, g, albedo,
                                               conductorURoughness, conductorVRoughness, conductorEta, k, reflectance,
                                               displacement, normalMap, remapRoughness, maxDepth, nSamples);
        }

        // CoatedConductorMaterial Private Members
        private FloatTexture displacement;
        private Image normalMap;
        private FloatTexture interfaceURoughness, interfaceVRoughness, thickness;
        private Spectrum interfaceEta;
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
            return new BSDF(ctx.ns, ctx.dpdus, buf.Alloc(bxdf));
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
            return new BSDF(ctx.ns, ctx.dpdus, buf.Alloc(bxdf));
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
            return new BSDF(ctx.ns, ctx.dpdus, buf.Alloc(bxdf));
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
        public static Material Create(string name, TextureParameterDictionary parameters, Image normalMap,
                                      SortedDictionary<string, Material> namedMaterials, FileLoc loc, Allocator alloc)
        {
            Material material = null;
            if (name.Length == 0 || name == "none")
            {
                pbrt.Warning(loc, pbrt.StringPrintf("Material \"%s\" is deprecated; use \"interface\" instead.", name));
                return null;
            }
            else if (name == "interface")
                return null;
            else if (name == "diffuse")
                material = DiffuseMaterial.Create(parameters, normalMap, loc, alloc);
            else if (name == "coateddiffuse")
                material = CoatedDiffuseMaterial.Create(parameters, normalMap, loc, alloc);
            else if (name == "coatedconductor")
                material = CoatedConductorMaterial.Create(parameters, normalMap, loc, alloc);
            else if (name == "diffusetransmission")
                material = DiffuseTransmissionMaterial.Create(parameters, normalMap, loc, alloc);
            else if (name == "dielectric")
                material = DielectricMaterial.Create(parameters, normalMap, loc, alloc);
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
