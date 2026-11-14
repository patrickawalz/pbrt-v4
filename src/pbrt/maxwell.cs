// (not in pbrt-v4) maxwell.cs
//
// MaxwellMaterial ("maxwell"): a material built as Maxwell Render builds its own, from layers that hold BSDFs, on the lobes and
// the two operations of this branch (combine.cs). It is this renderer's version of that structure, not Next Limit's code.
//
// A material is up to 8 layers, "layer0" the lowest. A layer is up to 4 BSDFs, mixed by their weights (which are relative, as
// in Maxwell), and it lies on the layers below it in one of three ways ("string layerN.blend"):
//   "normal"     it covers a share "layerN.opacity" of what is below (a mix: Maxwell's normal mode)
//   "additive"   it is added to what is below with the weight "layerN.opacity" (Maxwell's additive mode; energy is conserved
//                only if the user sees to it)
//   "stack"      (ours) it is an interface on what is below, with a medium between the two, and the light is followed between
//                them by a random walk (StackBxDF): the layer has to transmit. On the share "layerN.opacity" of the surface.
// What a first layer with an opacity below 1 leaves uncovered lets the light through unchanged.
//
// The medium under a stacked layer, between it and the layers below ("layerN.medium."):
//   spectrum absorption     the colour of white light that crossed it along the normal (Beer's law by wavelength), or
//   float thickness         its optical thickness, with "spectrum albedo" and "float g" (Henyey-Greenstein)
//   integer scatterings     the scatterings in it that are followed: 1 for single scattering, 0 (default) for all of them
// so one material can hold a medium that is scattered in once and another with every scattering.
//
// A BSDF ("layerN.bsdfM."):
//   float weight            1
//   string type             "maxwell" (default), or a lobe of this branch by name: "diffuse", "conductor", "dielectric",
//                           "thindielectric", "diffusetransmission", "fuzz", "velvet"
//   spectrum reflectance0, reflectance90     the colour reflected along the normal and at grazing (0.5; 1)
//   spectrum transmittance  the colour transmitted (black: opaque)
//   float nd                the index (1.5); it shapes the curve between the two reflectances
//   spectrum eta, k         a complex index: the BSDF is that conductor, with its exact Fresnel reflectance
//   bool forcefresnel       the Fresnel reflectance of nd itself, in place of the two reflectances
//   float roughness         0 to 100, as in Maxwell: alpha = (roughness / 100)^2, and 100 is a Lambertian of reflectance0
//   float anisotropy        0 to 1
//   float coating.thickness, coating.nd      a thin film on the BSDF (nanometres; 0: none)
//   string lobe             "microfacet" or "multiscatter" (every reflection on the microsurface: conductor and dielectric)
//   string distribution     "ggx", "beckmann", "studentt" ("float gamma")
// As "maxwell", the BSDF is: a Lambertian at a roughness of 100; the conductor with eta and k; a dielectric interface of the
// index nd where it transmits (with the transmittance as the tint of what passes); and otherwise an opaque glossy lobe whose
// reflectance goes from reflectance0 to reflectance90 along the Fresnel curve of nd (GeneralizedSchlickBxDF).
//
//   float attenuation       the attenuation distance: the inside of the object is then a volume of this material (Material.
//                           SetOwnVolume), absorbing so that white light has the colour of the transmittance after that distance,
//                           with "spectrum scattering" (the scattering coefficient times the distance) and "float asymmetry".
//                           The first BSDF with an attenuation gives the volume; "string volume_scattering" "multiple", "single"
//                           or "absorption" is how the "production" integrator integrates it.
//
// Not here: the rotation of the anisotropy, R2, emitters and displacement per layer.

using System;
using System.Collections.Generic;

namespace pbrt
{
    public sealed class MaxwellBsdf
    {
        public string type = "maxwell";
        public FloatTexture weight, roughness, anisotropy;
        public SpectrumTexture reflectance0, reflectance90, transmittance, eta, k;
        public Spectrum nd;
        public bool forceFresnel = false, multiScatter = false, interior = false;
        public Float coatingThickness = 0, coatingNd = 1.4f, gamma = 2;
        public MicrofacetDistributionType distribution = MicrofacetDistributionType.TrowbridgeReitz;
    }

    public sealed class MaxwellLayer
    {
        public List<MaxwellBsdf> bsdfs = new List<MaxwellBsdf>();
        public FloatTexture opacity;
        public int blend = 0;  // 0 normal, 1 additive, 2 stack
        public SpectrumTexture absorption, albedo;
        public Float thickness = 0, g = 0;
        public int scatterings = 0;
    }

    public sealed unsafe class MaxwellMaterial : Material
    {
        public const int MaxLayers = 8, MaxBsdfs = 4;

        public List<MaxwellLayer> layers = new List<MaxwellLayer>();
        public int scatterDepth = 16, maxDepth = 32, nSamples = 1;
        // ("bool reciprocal") the walks of the stacked layers evaluated from both directions (LayeredBxDF.reciprocal)
        public bool reciprocal = false;
        public FloatTexture displacement;
        public Image normalMap;

        public static string Name() { return "MaxwellMaterial"; }

        public override FloatTexture GetDisplacement() { return displacement; }
        public override Image GetNormalMap() { return normalMap; }
        public override bool HasSubsurfaceScattering() { return false; }

        public override BSSRDF GetBSSRDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx, ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            return null;
        }

        public override bool CanEvaluateTextures<TextureEvaluator>(TextureEvaluator texEval)
        {
            List<FloatTexture> floats = new List<FloatTexture>();
            List<SpectrumTexture> spectra = new List<SpectrumTexture>();
            foreach (MaxwellLayer layer in layers)
            {
                floats.Add(layer.opacity);
                spectra.Add(layer.absorption);
                spectra.Add(layer.albedo);
                foreach (MaxwellBsdf b in layer.bsdfs)
                {
                    floats.Add(b.weight);
                    floats.Add(b.roughness);
                    floats.Add(b.anisotropy);
                    spectra.Add(b.reflectance0);
                    spectra.Add(b.reflectance90);
                    spectra.Add(b.transmittance);
                    spectra.Add(b.eta);
                    spectra.Add(b.k);
                }
            }
            return texEval.CanEvaluate(floats.ToArray(), spectra.ToArray());
        }

        public override string ToString() { return StringPrintf("[ MaxwellMaterial layers: %d ]", layers.Count); }

        private BxDF Lobe<TextureEvaluator>(MaxwellBsdf b, TextureEvaluator texEval, MaterialEvalContext ctx, ref SampledWavelengths lambda)
            where TextureEvaluator : ITextureEvaluator
        {
            SampledSpectrum r0 = b.reflectance0 != null ? Clamp(texEval.Apply(b.reflectance0, ctx, lambda), 0, 1) : new SampledSpectrum(0.5f);
            SampledSpectrum r90 = b.reflectance90 != null ? Clamp(texEval.Apply(b.reflectance90, ctx, lambda), 0, 1) : new SampledSpectrum(1f);
            SampledSpectrum t = b.transmittance != null ? Clamp(texEval.Apply(b.transmittance, ctx, lambda), 0, 1) : new SampledSpectrum(0f);
            Float roughness = Clamp((b.roughness != null ? texEval.Apply(b.roughness, ctx) : 0) / 100, 0, 1);
            Float anisotropy = Clamp(b.anisotropy != null ? texEval.Apply(b.anisotropy, ctx) : 0, 0, 1);
            Float alpha = roughness * roughness;
            Float ax = alpha * std.sqrt(2 / (1 + Sqr(1 - anisotropy))), ay = (1 - anisotropy) * ax;
            MicrofacetDistribution d = MicrofacetDistribution.Create(b.distribution, ax, ay, b.gamma);
            bool conductor = b.eta != null && b.k != null;
            bool transmits = (bool)t;
            Float nd = 1.5f;
            if (b.nd != null)
            {
                nd = b.nd.Evaluate(lambda[0]);
                if (transmits && !conductor && !(b.nd is ConstantSpectrum) && (b.type == "maxwell" || b.type == "dielectric"))
                {
                    lambda.TerminateSecondary();
                }
            }
            nd = std.max<Float>(nd, 1.0001f);
            bool film = b.coatingThickness > 0;
            ThinFilmStack stack = default;
            if (film)
            {
                stack = new ThinFilmStack(lambda, new Float[] { b.coatingNd }, new Float[] { b.coatingThickness });
            }
            bool walk = b.multiScatter && !d.EffectivelySmooth();
            MicrosurfaceHeight height = new MicrosurfaceHeightUniform();
            switch (b.type)
            {
            case "diffuse":
                return new EonDiffuseBxDF(r0, roughness);
            case "diffusetransmission":
                return new DiffuseTransmissionBxDF(r0, t);
            case "thindielectric":
                return new TintedBxDF(new ThinDielectricBxDF(nd), new SampledSpectrum(1f), transmits ? t : new SampledSpectrum(1f));
            case "fuzz":
                return new SGGXSheenBxDF(r0, std.max<Float>(roughness, 0.01f), 1, std.max(scatterDepth, 16));
            case "velvet":
                return new VelvetBxDF(r0, std.max<Float>(roughness, 0.02f));
            case "conductor":
                conductor = true;
                break;
            case "dielectric":
                transmits = true;
                if (!(bool)t)
                {
                    t = new SampledSpectrum(1f);
                }
                break;
            }
            if (conductor)
            {
                SampledSpectrum eta = b.eta != null ? texEval.Apply(b.eta, ctx, lambda) : new SampledSpectrum(1f);
                SampledSpectrum k = b.k != null ? texEval.Apply(b.k, ctx, lambda) : 2 * Sqrt(Clamp(r0, 0, 0.9999f)) / Sqrt(ClampZero(new SampledSpectrum(1f) - Clamp(r0, 0, 0.9999f)));
                if (walk)
                {
                    return film ? new MultiScatterConductorBxDF(d, height, eta, k, scatterDepth, stack) : new MultiScatterConductorBxDF(d, height, eta, k, scatterDepth);
                }
                if (film)
                {
                    return new ThinFilmBxDF(stack, 1, eta, k, true, d);
                }
                return new ConductorBxDF(d, eta, k);
            }
            if (b.type == "maxwell" && roughness >= 1)
            {
                return new DiffuseBxDF(r0);
            }
            if (transmits)
            {
                BxDF glass;
                if (walk)
                {
                    glass = film ? new MultiScatterDielectricBxDF(d, height, nd, scatterDepth, stack) : new MultiScatterDielectricBxDF(d, height, nd, scatterDepth);
                }
                else if (film)
                {
                    glass = new ThinFilmBxDF(stack, nd, new SampledSpectrum(1f), new SampledSpectrum(0f), false, d);
                }
                else
                {
                    glass = new DielectricBxDF(nd, d);
                }
                bool tinted = false;
                for (int c = 0; c < NSpectrumSamples; ++c)
                {
                    tinted |= t[c] != 1;
                }
                // (with an attenuation distance the colour is that of the volume inside, not of the interface)
                return tinted && !b.interior ? new TintedBxDF(glass, new SampledSpectrum(1f), t) : glass;
            }
            if (b.forceFresnel)
            {
                return new GeneralizedSchlickBxDF(d, new SampledSpectrum(FrDielectric(1, nd)), new SampledSpectrum(1f), 5, nd);
            }
            return new GeneralizedSchlickBxDF(d, r0, r90, 5, nd);
        }

        public BxDF GetBxDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx, ref SampledWavelengths lambda)
            where TextureEvaluator : ITextureEvaluator
        {
            BxDF result = null;
            for (int l = 0; l < layers.Count; ++l)
            {
                MaxwellLayer layer = layers[l];
                BxDF[] parts = new BxDF[layer.bsdfs.Count];
                Float[] weights = new Float[layer.bsdfs.Count];
                Float total = 0;
                for (int i = 0; i < parts.Length; ++i)
                {
                    weights[i] = std.max<Float>(layer.bsdfs[i].weight != null ? texEval.Apply(layer.bsdfs[i].weight, ctx) : 1, 0);
                    total += weights[i];
                    parts[i] = weights[i] > 0 ? Lobe(layer.bsdfs[i], texEval, ctx, ref lambda) : null;
                }
                if (!(total > 0))
                {
                    continue;
                }
                BxDF own = null;
                if (parts.Length == 1)
                {
                    own = parts[0];
                }
                else
                {
                    for (int i = 0; i < parts.Length; ++i)
                    {
                        weights[i] /= total;
                    }
                    own = new MixBxDF(parts, weights);
                }
                Float opacity = Clamp(layer.opacity != null ? texEval.Apply(layer.opacity, ctx) : 1, 0, 1);
                if (opacity <= 0)
                {
                    continue;
                }
                if (result == null)
                {
                    result = opacity < 1 ? MixBxDF.Of(new OSLTransparentBxDF(), own, opacity) : own;
                    continue;
                }
                switch (layer.blend)
                {
                case 1:
                    result = new MixBxDF(new BxDF[] { result, own }, new Float[] { 1, opacity });
                    break;
                case 2:
                    {
                        bool twoSided = (result.Flags() & BxDFFlags.Transmission) == 0;
                        StackBxDF stack;
                        if (layer.thickness > 0 && layer.albedo != null)
                        {
                            stack = new StackBxDF(own, result, layer.thickness, Clamp(texEval.Apply(layer.albedo, ctx, lambda), 0, 1), layer.g, maxDepth, nSamples, twoSided);
                        }
                        else
                        {
                            SampledSpectrum depth = new SampledSpectrum(0f);
                            if (layer.absorption != null)
                            {
                                SampledSpectrum colour = Clamp(texEval.Apply(layer.absorption, ctx, lambda), 1e-6f, 1);
                                for (int c = 0; c < NSpectrumSamples; ++c)
                                {
                                    depth[c] = -std.log(colour[c]);
                                }
                            }
                            stack = StackBxDF.Absorbing(own, result, depth, maxDepth, nSamples, twoSided);
                        }
                        if (layer.scatterings > 0)
                        {
                            stack.mediumOrder = layer.scatterings;
                        }
                        result = MixBxDF.Of(result, stack, opacity);
                    }
                    break;
                default:
                    result = MixBxDF.Of(result, own, opacity);
                    break;
                }
            }
            if (reciprocal && result != null)
            {
                StackBxDF.SetReciprocal(result, true);
            }
            return result ?? new DiffuseBxDF(new SampledSpectrum(0f));
        }

        public override BSDF GetBSDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx, ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            return new BSDF(ctx.n, ctx.ns, ctx.dpdus, buf.Alloc(GetBxDF(texEval, ctx, ref lambda)));
        }

        public static MaxwellMaterial Create(TextureParameterDictionary parameters, Image normalMap, FileLoc loc, Allocator alloc)
        {
            MaxwellMaterial m = new MaxwellMaterial();
            bool interiorMade = false;
            for (int l = 0; l < MaxLayers; ++l)
            {
                string lp = "layer" + l + ".";
                MaxwellLayer layer = new MaxwellLayer();
                for (int i = 0; i < MaxBsdfs; ++i)
                {
                    string p = lp + "bsdf" + i + ".";
                    MaxwellBsdf b = new MaxwellBsdf();
                    b.type = parameters.GetOneString(p + "type", "");
                    b.weight = parameters.GetFloatTextureOrNull(p + "weight", alloc);
                    b.roughness = parameters.GetFloatTextureOrNull(p + "roughness", alloc);
                    b.reflectance0 = parameters.GetSpectrumTextureOrNull(p + "reflectance0", SpectrumType.Albedo, alloc);
                    b.eta = parameters.GetSpectrumTextureOrNull(p + "eta", SpectrumType.Unbounded, alloc);
                    b.k = parameters.GetSpectrumTextureOrNull(p + "k", SpectrumType.Unbounded, alloc);
                    b.transmittance = parameters.GetSpectrumTextureOrNull(p + "transmittance", SpectrumType.Albedo, alloc);
                    if (b.type == "" && b.weight == null && b.roughness == null && b.reflectance0 == null && b.eta == null && b.transmittance == null)
                    {
                        continue;
                    }
                    if (b.type == "")
                    {
                        b.type = "maxwell";
                    }
                    if (b.type != "maxwell" && b.type != "diffuse" && b.type != "conductor" && b.type != "dielectric" && b.type != "thindielectric" &&
                        b.type != "diffusetransmission" && b.type != "fuzz" && b.type != "velvet")
                    {
                        ErrorExit(loc, StringPrintf("\"%stype\": unknown BSDF \"%s\"", p, b.type));
                    }
                    if ((b.eta == null) != (b.k == null))
                    {
                        ErrorExit(loc, StringPrintf("\"%seta\" and \"%sk\" must be given together", p, p));
                    }
                    b.reflectance90 = parameters.GetSpectrumTextureOrNull(p + "reflectance90", SpectrumType.Albedo, alloc);
                    b.anisotropy = parameters.GetFloatTextureOrNull(p + "anisotropy", alloc);
                    b.nd = ParametricSpectra.Index(parameters, p + "nd", alloc);
                    b.forceFresnel = parameters.GetOneBool(p + "forcefresnel", false);
                    b.coatingThickness = parameters.GetOneFloat(p + "coating.thickness", 0f);
                    b.coatingNd = parameters.GetOneFloat(p + "coating.nd", 1.4f);
                    string lobe = parameters.GetOneString(p + "lobe", "microfacet");
                    if (lobe != "microfacet" && lobe != "multiscatter")
                    {
                        ErrorExit(loc, StringPrintf("\"%slobe\" must be \"microfacet\" or \"multiscatter\"", p));
                    }
                    b.multiScatter = lobe == "multiscatter";
                    b.distribution = MicrofacetDistribution.ParseType(parameters.GetOneString(p + "distribution", "ggx"), loc);
                    b.gamma = parameters.GetOneFloat(p + "gamma", 2f);
                    Float attenuation = parameters.GetOneFloat(p + "attenuation", 0f);
                    if (attenuation > 0 && !interiorMade)
                    {
                        // The inside of the object: white light has the colour of the transmittance after the attenuation
                        // distance; "scattering" is the scattering coefficient times that distance
                        Spectrum colour = parameters.GetOneSpectrum(p + "transmittance", new ConstantSpectrum(1f), SpectrumType.Albedo, alloc);
                        Spectrum scatter = parameters.GetOneSpectrum(p + "scattering", new ConstantSpectrum(0f), SpectrumType.Albedo, alloc);
                        Float g = Clamp(parameters.GetOneFloat(p + "asymmetry", 0f), -0.99f, 0.99f);
                        m.SetOwnVolume(MakeHomogeneousVolume((Float lambda) => -std.log(Clamp(colour.Evaluate(lambda), 1e-4f, 1)) / attenuation,
                                                             (Float lambda) => scatter.Evaluate(lambda) / attenuation, g,
                                                             parameters.GetOneString("volume_scattering", "multiple"), loc, alloc));
                        b.interior = true;
                        interiorMade = true;
                    }
                    layer.bsdfs.Add(b);
                }
                if (layer.bsdfs.Count == 0)
                {
                    continue;
                }
                layer.opacity = parameters.GetFloatTextureOrNull(lp + "opacity", alloc);
                string blend = parameters.GetOneString(lp + "blend", "normal");
                if (blend == "normal")
                {
                    layer.blend = 0;
                }
                else if (blend == "additive")
                {
                    layer.blend = 1;
                }
                else if (blend == "stack")
                {
                    layer.blend = 2;
                }
                else
                {
                    ErrorExit(loc, StringPrintf("\"%sblend\" must be \"normal\", \"additive\" or \"stack\"", lp));
                }
                layer.absorption = parameters.GetSpectrumTextureOrNull(lp + "medium.absorption", SpectrumType.Albedo, alloc);
                layer.albedo = parameters.GetSpectrumTextureOrNull(lp + "medium.albedo", SpectrumType.Albedo, alloc);
                layer.thickness = parameters.GetOneFloat(lp + "medium.thickness", 0f);
                layer.g = Clamp(parameters.GetOneFloat(lp + "medium.g", 0f), -0.99f, 0.99f);
                layer.scatterings = parameters.GetOneInt(lp + "medium.scatterings", 0);
                m.layers.Add(layer);
            }
            if (m.layers.Count == 0)
            {
                ErrorExit(loc, "\"maxwell\": no layer (\"layer0.bsdf0.roughness\", ...)");
            }
            m.scatterDepth = parameters.GetOneInt("scatterdepth", 16);
            m.maxDepth = parameters.GetOneInt("maxdepth", 32);
            m.nSamples = parameters.GetOneInt("nsamples", 1);
            m.reciprocal = parameters.GetOneBool("reciprocal", false);
            m.displacement = parameters.GetFloatTextureOrNull("displacement", alloc);
            m.normalMap = normalMap;
            return m;
        }
    }
}
