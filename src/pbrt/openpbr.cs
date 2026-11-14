// (not in pbrt-v4) openpbr.cs
//
// OpenPBRMaterial ("openpbr"): the structure of OpenPBR (Portsmouth, Kutz and Hill, "OpenPBR: Novel Features and Implementation
// Details", SIGGRAPH 2025 course; the specification of the Academy Software Foundation), built from the lobes of this branch
// with the two operations of combine.cs. The tree is that of the specification (its Eq. 22):
//
//   M_PBR             = mix(nothing, M_surface, geometry_opacity)
//   M_surface         = layer(M_coated-base, fuzz, fuzz_weight)
//   M_coated-base     = layer(M_base-substrate, coat, coat_weight)
//   M_base-substrate  = mix(M_dielectric-base, metal, base_metalness)
//   M_dielectric-base = mix(M_opaque-base, translucent base, transmission_weight)
//   M_opaque-base     = mix(M_glossy-diffuse, subsurface, subsurface_weight)
//   M_glossy-diffuse  = layer(diffuse, gloss)
//
// with  mix(A, B, w)   = MixBxDF: (1 - w) A + w B
//       layer(A, L, w) = mix(A, StackBxDF(L over A), w)
// so the light between the layers is followed (the inter-reflections under the coat that darken it, the total internal
// reflection, the roughening of what is under a rough layer, the longer path in an absorbing coat towards grazing): none of
// these is a formula here. This is what the specification calls full conformance, and it costs a random walk for each layer.
//
// The lobes:
//   diffuse           EonDiffuseBxDF (base_color base_weight, base_diffuse_roughness)
//   gloss, translucent base, coat   a dielectric interface (below)
//   metal             a conductor interface, with the complex index when "metal_eta" and "metal_k" are given (the exact Fresnel
//                     equations; the F82-tint model of the specification is not used), else the conductor whose reflectance at
//                     normal incidence is base_color base_weight
//   subsurface        the interface over a scattering medium (subsurface_color to its albedo by the fit of the specification,
//                     subsurface_scatter_anisotropy), walked: a BSDF, the light leaves where it entered
//   fuzz              SGGXSheenBxDF: the index-matched slab of fibre-like SGGX flakes of unit optical thickness that the
//                     specification describes, walked with every scattering (where it fits an LTC)
//   thin film         the Airy sum over the reflections in the film, with complex amplitudes and both polarisations, at each
//                     microfacet and for the wavelengths of the path (ThinFilmStack: what the paper's appendix E sets out), on
//                     the gloss and metal interfaces (thin_film_weight, _thickness in micrometres, _ior), mixed with the bare
//                     interface by its weight. Under a coat the medium above the film is the coat.
//   thin walled       transmission: ThinDielectricBxDF; subsurface: half the light reflected and half transmitted, diffusely
//
// An interface, for each of "specular", "metal" and "coat" (the prefix P):
//   string P_lobe            "microfacet" (one scattering, pbrt), "multiscatter" (the walk of Heitz et al.), "facetforge"
//   string P_distribution    "ggx", "beckmann", "studentt" (with float P_gamma); more distributions with P_distribution1 ...
//                            (distributionblend.cs: P_weight, P_roughness1, P_layering)
//   integer scatterdepth     the reflections of a walk on a microsurface (16)
// and what the paper lists as planned (its section 3.4), on the gloss and the metal:
//   float specular_haze, specular_haze_spread         a second lobe of the roughness r + spread (1 - r), mixed in with `haze`
//   float specular_retroreflectivity                  the share of the light sent back (RetroreflectiveBxDF, FacetForgeBxDF)
//
// The inside of the object. With "float transmission_depth" above 0 the material has a volume (Material.SetOwnVolume: the shapes
// that use it bound it, as with "string volume"): the homogeneous medium whose extinction makes white light transmission_color
// after that depth, with the scattering coefficient "rgb transmission_scatter" / depth and "float
// transmission_scatter_anisotropy", as the specification defines them; the interface is then not tinted. With "string
// subsurface_mode" "volume" the subsurface is such a volume too, under the gloss interface: mean free path "float
// subsurface_radius" times "rgb subsurface_radius_scale" (1; 1 0.5 0.25), the albedo that gives subsurface_color, so the light
// leaves elsewhere than it entered ("bsdf", the default, is the slab above: it leaves where it entered). The two are one
// medium, mixed by the weights. "string volume_scattering" "multiple", "single" or "absorption" is how the volume is
// integrated by the "production" integrator (productionvolume.cs); volpath and bdpt follow every scattering. These parameters
// and the two weights are read as constants for the volume.
//
// Not here: emission (an area light in pbrt), coat_darkening below 1, and dispersion by an Abbe number.
//
// "bool reciprocal": the walks between the layers are evaluated from both directions and averaged, so that every evaluation
// of f is reciprocal (their mean is without it), at twice the cost.
//
// Other parameters: "string roughness_mapping" "openpbr" (alpha = r^2), "pbrt" or "alpha"; "float subsurface_depth" (the optical
// thickness of the subsurface medium, 30); "integer maxdepth" (events of a walk between layers, 32), "integer nsamples" (1).

using System;
using System.Collections.Generic;

namespace pbrt
{
    public enum OpenPBRLobe
    {
        Microfacet,
        MultiScatter,
        FacetForge
    }

    public sealed class OpenPBRInterface
    {
        public OpenPBRLobe lobe = OpenPBRLobe.Microfacet;
        public MicrofacetDistributionType distribution = MicrofacetDistributionType.TrowbridgeReitz;
        public Float gamma = 2;
        public DistributionBlend blend;

        public static OpenPBRInterface Parse(TextureParameterDictionary parameters, string prefix, FileLoc loc, Allocator alloc)
        {
            OpenPBRInterface i = new OpenPBRInterface();
            string name = parameters.GetOneString(prefix + "lobe", "microfacet");
            if (name == "microfacet")
            {
                i.lobe = OpenPBRLobe.Microfacet;
            }
            else if (name == "multiscatter")
            {
                i.lobe = OpenPBRLobe.MultiScatter;
            }
            else if (name == "facetforge")
            {
                i.lobe = OpenPBRLobe.FacetForge;
            }
            else
            {
                pbrt.ErrorExit(loc, pbrt.StringPrintf("\"%slobe\" must be \"microfacet\", \"multiscatter\" or \"facetforge\"", prefix));
            }
            i.distribution = MicrofacetDistribution.ParseType(parameters.GetOneString(prefix + "distribution", "ggx"), loc);
            i.gamma = parameters.GetOneFloat(prefix + "gamma", 2f);
            i.blend = DistributionBlend.Parse(parameters, prefix, loc, alloc);
            if (i.blend != null && i.blend.layered && i.lobe == OpenPBRLobe.MultiScatter)
            {
                pbrt.ErrorExit(loc, "\"layering\" \"asymmetric\" with every reflection is the \"facetforge\" lobe");
            }
            return i;
        }
    }

    public sealed unsafe class OpenPBRMaterial : Material
    {
        // The parameters of the specification (null: its default)
        public FloatTexture baseWeight, baseDiffuseRoughness, baseMetalness;
        public SpectrumTexture baseColor;
        public FloatTexture specularWeight, specularRoughness, specularAnisotropy;
        public SpectrumTexture specularColor;
        public Spectrum specularIor;
        public FloatTexture transmissionWeight;
        public SpectrumTexture transmissionColor;
        public FloatTexture subsurfaceWeight;
        public SpectrumTexture subsurfaceColor;
        public Float subsurfaceAnisotropy = 0, subsurfaceDepth = 30;
        public FloatTexture coatWeight, coatRoughness, coatAnisotropy;
        public SpectrumTexture coatColor;
        public Spectrum coatIor;
        public FloatTexture fuzzWeight, fuzzRoughness;
        public SpectrumTexture fuzzColor;
        public FloatTexture thinFilmWeight, thinFilmThickness;
        public Float thinFilmIor = 1.4f;
        public FloatTexture opacity;
        public bool thinWalled = false;
        // The inside of the object is a volume of this material (Create): the colour of the transmission is that of the volume,
        // and the subsurface is what the volume scatters under the interface
        public bool interiorTransmission = false, subsurfaceVolume = false;
        // Beyond the specification
        public SpectrumTexture metalEta, metalK;
        public FloatTexture specularHaze, specularHazeSpread;
        public Float specularRetro = 0;
        public OpenPBRInterface specular, metal, coat;
        public int scatterDepth = 16, maxDepth = 32, nSamples = 1;
        // The walks between the layers evaluated from both directions: each evaluation reciprocal, at twice the cost
        public bool reciprocal = false;
        public int roughnessMapping = 0;  // 0 OpenPBR, 1 pbrt, 2 alpha
        public FloatTexture displacement;
        public Image normalMap;

        public static string Name() { return "OpenPBRMaterial"; }

        public override FloatTexture GetDisplacement() { return displacement; }
        public override Image GetNormalMap() { return normalMap; }
        public override bool HasSubsurfaceScattering() { return false; }

        public override BSSRDF GetBSSRDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx, ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            return null;
        }

        public override bool CanEvaluateTextures<TextureEvaluator>(TextureEvaluator texEval)
        {
            return texEval.CanEvaluate(new FloatTexture[] { baseWeight, baseDiffuseRoughness, baseMetalness, specularWeight, specularRoughness, specularAnisotropy,
                                                            transmissionWeight, subsurfaceWeight, coatWeight, coatRoughness, coatAnisotropy, fuzzWeight,
                                                            fuzzRoughness, thinFilmWeight, thinFilmThickness, opacity, specularHaze, specularHazeSpread },
                                       new SpectrumTexture[] { baseColor, specularColor, transmissionColor, subsurfaceColor, coatColor, fuzzColor, metalEta,
                                                               metalK });
        }

        public override string ToString()
        {
            return StringPrintf("[ OpenPBRMaterial thin walled: %s scatter depth: %d ]", thinWalled, scatterDepth);
        }

        private Float Alpha(Float roughness)
        {
            roughness = Clamp(roughness, 0, 1);
            switch (roughnessMapping)
            {
            case 1:
                return TrowbridgeReitzDistribution.RoughnessToAlpha(roughness);
            case 2:
                return roughness;
            default:
                return roughness * roughness;
            }
        }

        // The roughnesses along the tangent and the bitangent (the specification: alpha_t = alpha sqrt(2 / (1 + (1 - a)^2)),
        // alpha_b = (1 - a) alpha_t)
        private void Alphas(Float roughness, Float anisotropy, out Float ax, out Float ay)
        {
            Float alpha = Alpha(roughness);
            anisotropy = Clamp(anisotropy, 0, 1);
            ax = alpha * std.sqrt(2 / (1 + Sqr(1 - anisotropy)));
            ay = (1 - anisotropy) * ax;
        }

        // What an evaluation needs, gathered once
        private sealed class Point<TextureEvaluator>
            where TextureEvaluator : ITextureEvaluator
        {
            public TextureEvaluator texEval;
            public MaterialEvalContext ctx;
            public SampledWavelengths lambda;
            public Float roughness, anisotropy, haze, hazeSpread, filmWeight, filmThickness;
            // The index of the medium above the interface being built (1, or that of the coat)
            public Float outside = 1;
        }

        private static ff.Kind ForgeKind(MicrofacetDistributionType type)
        {
            switch (type)
            {
            case MicrofacetDistributionType.Beckmann:
                return ff.Kind.Beckmann;
            case MicrofacetDistributionType.StudentT:
                return ff.Kind.StudentT;
            default:
                return ff.Kind.GGX;
            }
        }

        // One interface of the roughness given: a conductor of the index (ce, ck), or a dielectric of the index eta
        private BxDF Lobe<TextureEvaluator>(Point<TextureEvaluator> p, OpenPBRInterface options, bool conductor, Float eta, SampledSpectrum ce, SampledSpectrum ck,
                                             Float roughness, Float anisotropy, bool film, Float retro)
            where TextureEvaluator : ITextureEvaluator
        {
            Float ax, ay;
            Alphas(roughness, anisotropy, out ax, out ay);
            ThinFilmStack stack = default;
            if (film)
            {
                // The film lies between the medium above (the air, or the coat) and the substrate, and the indices of the
                // interface are relative to the medium above: so is that of the film, and the wavelength in that medium is
                // shorter by its index (the phase of a round trip in the film, 4 pi n2 d cos(theta2) / lambda, is unchanged)
                stack = new ThinFilmStack(p.lambda, new Float[] { std.max<Float>(thinFilmIor / p.outside, 1.0001f) }, new Float[] { p.filmThickness * p.outside });
            }
            OpenPBRLobe kind = options.lobe;
            if (kind == OpenPBRLobe.FacetForge)
            {
                ff.Surface surface = new ff.Surface();
                surface.ndf.n = 1;
                surface.ndf.c[0].InitHeightfield(ForgeKind(options.distribution), std.max<Float>(ax, 1e-3f), std.max<Float>(ay, 1e-3f), options.gamma);
                surface.ndf.c[0].w = 1;
                if (options.blend != null)
                {
                    int count = Math.Min(options.blend.types.Count, ff.kMaxComps - 1);
                    surface.ndf.c[0].w = options.blend.weights[0];
                    for (int i = 0; i < count; ++i)
                    {
                        Float bx = std.max<Float>(Alpha(p.texEval.Apply(options.blend.uRoughness[i], p.ctx)), 1e-3f);
                        Float by = std.max<Float>(Alpha(p.texEval.Apply(options.blend.vRoughness[i], p.ctx)), 1e-3f);
                        surface.ndf.c[i + 1].InitHeightfield(ForgeKind(options.blend.types[i]), bx, by, options.blend.gammas[i]);
                        surface.ndf.c[i + 1].w = options.blend.weights[i + 1];
                    }
                    surface.ndf.n = count + 1;
                    surface.ndf.Normalize();
                    surface.ndf.layered = options.blend.layered;
                }
                surface.maxWalk = scatterDepth;
                surface.facet.kind = conductor ? ff.FacetKind.Conductor : ff.FacetKind.Dielectric;
                if (conductor)
                {
                    surface.facet.eta = ce;
                    surface.facet.k = ck;
                }
                else
                {
                    surface.facet.eta = new SampledSpectrum(eta);
                }
                if (film)
                {
                    surface.facet.stack = stack;
                    surface.facet.hasStack = true;
                }
                FacetForgeBxDF forge = new FacetForgeBxDF(surface, null, eta, !conductor);
                forge.SetRetro(retro);
                return forge;
            }
            MicrofacetDistribution d = MicrofacetDistribution.Create(options.distribution, ax, ay, options.gamma);
            if (options.blend != null)
            {
                d = DistributionBlend.Create(options.blend, d, p.texEval, p.ctx, roughnessMapping == 1);
            }
            if (kind == OpenPBRLobe.MultiScatter && !d.EffectivelySmooth() && !(!conductor && eta == 1))
            {
                MicrosurfaceHeight height = new MicrosurfaceHeightUniform();
                if (conductor)
                {
                    return film ? new MultiScatterConductorBxDF(d, height, ce, ck, scatterDepth, stack) : new MultiScatterConductorBxDF(d, height, ce, ck, scatterDepth);
                }
                return film ? new MultiScatterDielectricBxDF(d, height, eta, scatterDepth, stack) : new MultiScatterDielectricBxDF(d, height, eta, scatterDepth);
            }
            if (film)
            {
                return conductor ? new ThinFilmBxDF(stack, 1, ce, ck, true, d) : new ThinFilmBxDF(stack, eta, new SampledSpectrum(1f), new SampledSpectrum(0f), false, d);
            }
            if (conductor)
            {
                ConductorBxDF c = new ConductorBxDF(d, ce, ck);
                return retro > 0 && !d.EffectivelySmooth() ? new RetroreflectiveBxDF(c, retro) : c;
            }
            DielectricBxDF g = new DielectricBxDF(eta, d);
            return retro > 0 && !d.EffectivelySmooth() && eta != 1 ? new RetroreflectiveBxDF(g, retro) : g;
        }

        // The interface with what the gloss and the metal can carry: the film on a share of it, and the haze lobe
        private BxDF Specular<TextureEvaluator>(Point<TextureEvaluator> p, OpenPBRInterface options, bool conductor, Float eta, SampledSpectrum ce, SampledSpectrum ck)
            where TextureEvaluator : ITextureEvaluator
        {
            BxDF core = Lobe(p, options, conductor, eta, ce, ck, p.roughness, p.anisotropy, false, specularRetro);
            if (p.filmWeight > 0 && p.filmThickness > 0)
            {
                core = MixBxDF.Of(core, Lobe(p, options, conductor, eta, ce, ck, p.roughness, p.anisotropy, true, 0), p.filmWeight);
            }
            if (p.haze > 0 && p.hazeSpread > 0)
            {
                Float wide = p.roughness + p.hazeSpread * (1 - p.roughness);
                core = MixBxDF.Of(core, Lobe(p, options, conductor, eta, ce, ck, wide, p.anisotropy, false, 0), p.haze);
            }
            return core;
        }

        private static SampledSpectrum White() { return new SampledSpectrum(1f); }

        // The albedo of the medium whose surface has this colour (the fit to van de Hulst that the specification uses)
        public static SampledSpectrum SubsurfaceAlbedo(SampledSpectrum colour)
        {
            SampledSpectrum albedo = new SampledSpectrum(0f);
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                Float x = colour[i];
                Float s = 4.09712f + 4.20863f * x - std.sqrt(9.59217f + 41.6808f * x + 17.7126f * x * x);
                albedo[i] = Clamp(1 - s * s, 0, 1);
            }
            return albedo;
        }

        public BxDF GetBxDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx, ref SampledWavelengths lambda)
            where TextureEvaluator : ITextureEvaluator
        {
            Float F(FloatTexture t, Float fallback) { return t != null ? texEval.Apply(t, ctx) : fallback; }
            SampledWavelengths wavelengths = lambda;
            SampledSpectrum S(SpectrumTexture t, Float fallback)
            {
                return t != null ? Clamp(texEval.Apply(t, ctx, wavelengths), 0, 1) : new SampledSpectrum(fallback);
            }

            Float wBase = Clamp(F(baseWeight, 1), 0, 1), metalness = Clamp(F(baseMetalness, 0), 0, 1);
            Float wSpecular = Clamp(F(specularWeight, 1), 0, 1);
            Float wTransmission = Clamp(F(transmissionWeight, 0), 0, 1), wSubsurface = Clamp(F(subsurfaceWeight, 0), 0, 1);
            Float wCoat = Clamp(F(coatWeight, 0), 0, 1), wFuzz = Clamp(F(fuzzWeight, 0), 0, 1), alpha = Clamp(F(opacity, 1), 0, 1);
            SampledSpectrum color = S(baseColor, 0.8f) * wBase;

            Point<TextureEvaluator> p = new Point<TextureEvaluator>();
            p.texEval = texEval;
            p.ctx = ctx;
            p.roughness = F(specularRoughness, 0.3f);
            p.anisotropy = F(specularAnisotropy, 0);
            p.haze = Clamp(F(specularHaze, 0), 0, 1);
            p.hazeSpread = Clamp(F(specularHazeSpread, 0), 0, 1);
            p.filmWeight = Clamp(F(thinFilmWeight, 0), 0, 1);
            p.filmThickness = 1000 * std.max<Float>(F(thinFilmThickness, 0.5f), 0);  // micrometres to nanometres

            // The indices: a dispersive one costs the secondary wavelengths where the light is refracted
            bool refracts = wTransmission > 0 && metalness < 1 && !thinWalled;
            Float eta = specularIor.Evaluate(lambda[0]);
            if (refracts && !(specularIor is ConstantSpectrum))
            {
                lambda.TerminateSecondary();
            }
            p.lambda = lambda;
            // specular_weight scales the reflectance at normal incidence, by the index (the specification)
            if (wSpecular < 1)
            {
                Float f0 = Sqr((eta - 1) / (eta + 1)) * wSpecular;
                Float root = std.sqrt(Clamp(f0, 0, 0.999f));
                eta = (1 + root) / (1 - root);
            }
            Float etaCoat = coatIor.Evaluate(lambda[0]);

            SampledSpectrum etaMetal, kMetal;
            if (metalEta != null && metalK != null)
            {
                etaMetal = texEval.Apply(metalEta, ctx, lambda);
                kMetal = texEval.Apply(metalK, ctx, lambda);
            }
            else
            {
                // the conductor with this reflectance at normal incidence (as pbrt's "reflectance")
                SampledSpectrum r = Clamp(color, 0, 0.9999f);
                etaMetal = new SampledSpectrum(1f);
                kMetal = 2 * Sqrt(r) / Sqrt(ClampZero(new SampledSpectrum(1f) - r));
            }
            SampledSpectrum tintSpecular = S(specularColor, 1), tintTransmission = interiorTransmission ? White() : S(transmissionColor, 1);
            bool transmissive = (wTransmission > 0 || ((thinWalled || subsurfaceVolume) && wSubsurface > 0)) && metalness < 1;
            bool twoSided = !transmissive;
            SampledSpectrum nothing = new SampledSpectrum(0f);

            // The base substrate as it is under a medium of the index `outside` (1: the air; the coat's under the coat)
            BxDF Substrate(Float outside)
            {
                p.outside = outside;
                Float relative = eta / outside;
                if (relative < 1)
                {
                    relative = 1 / relative;  // (the specification: no total internal reflection for a gloss rarer than the coat)
                }
                relative = std.max<Float>(relative, 1.0001f);
                BxDF Gloss()
                {
                    BxDF lobe = Specular(p, specular, false, relative, default, default);
                    bool tinted = false;
                    for (int c = 0; c < NSpectrumSamples; ++c)
                    {
                        tinted |= tintSpecular[c] != 1;
                    }
                    return tinted ? new TintedBxDF(lobe, tintSpecular, White()) : lobe;
                }
                BxDF dielectricBase = null;
                if (metalness < 1)
                {
                    BxDF opaque = null;
                    if (wTransmission < 1)
                    {
                        BxDF glossyDiffuse = null, subsurface = null;
                        if (wSubsurface < 1)
                        {
                            glossyDiffuse = StackBxDF.Absorbing(Gloss(), new EonDiffuseBxDF(color, Clamp(F(baseDiffuseRoughness, 0), 0, 1)), nothing, maxDepth, nSamples,
                                                                true);
                        }
                        if (wSubsurface > 0)
                        {
                            SampledSpectrum c = S(subsurfaceColor, 0.8f);
                            if (thinWalled)
                            {
                                subsurface = StackBxDF.Absorbing(Gloss(), new DiffuseTransmissionBxDF(c * 0.5f, c * 0.5f), nothing, maxDepth, nSamples, false);
                            }
                            else if (subsurfaceVolume)
                            {
                                // the interface alone: the light is scattered in the volume of the material, by the integrator
                                subsurface = Gloss();
                            }
                            else
                            {
                                SampledSpectrum albedo = SubsurfaceAlbedo(c);
                                subsurface = new StackBxDF(Gloss(), new DiffuseBxDF(nothing), subsurfaceDepth, albedo, Clamp(subsurfaceAnisotropy, -0.99f, 0.99f),
                                                           std.max(maxDepth, 256), nSamples, true);
                            }
                        }
                        opaque = MixBxDF.Of(glossyDiffuse, subsurface, wSubsurface);
                    }
                    BxDF translucent = null;
                    if (wTransmission > 0)
                    {
                        BxDF lobe = thinWalled ? new ThinDielectricBxDF(relative) : Specular(p, specular, false, relative, default, default);
                        translucent = new TintedBxDF(lobe, tintSpecular, tintTransmission);
                    }
                    dielectricBase = MixBxDF.Of(opaque, translucent, wTransmission);
                }
                BxDF conductor = null;
                if (metalness > 0)
                {
                    conductor = Specular(p, metal, true, 1, etaMetal / outside, kMetal / outside);
                    bool tinted = false;
                    for (int c = 0; c < NSpectrumSamples; ++c)
                    {
                        tinted |= tintSpecular[c] != 1;
                    }
                    if (tinted && metalEta == null)
                    {
                        // (the specification tints the edge of its metal with specular_color; with a complex index given, the index
                        // is the colour)
                        conductor = new TintedBxDF(conductor, tintSpecular * wSpecular + new SampledSpectrum(1 - wSpecular), White());
                    }
                }
                return MixBxDF.Of(dielectricBase, conductor, metalness);
            }

            BxDF surface = Substrate(1);
            if (wCoat > 0)
            {
                Float ax, ay;
                BxDF coatLobe = Lobe(p, coat, false, std.max<Float>(etaCoat, 1.0001f), default, default, F(coatRoughness, 0), F(coatAnisotropy, 0), false, 0);
                // coat_color is the square of the transmittance of the coat along the normal
                SampledSpectrum cc = S(coatColor, 1), depth = new SampledSpectrum(0f);
                for (int i = 0; i < NSpectrumSamples; ++i)
                {
                    depth[i] = -0.5f * std.log(std.max<Float>(cc[i], 1e-6f));
                }
                BxDF coated = StackBxDF.Absorbing(coatLobe, Substrate(etaCoat), depth, maxDepth, nSamples, twoSided);
                surface = MixBxDF.Of(surface, coated, wCoat);
            }
            if (wFuzz > 0)
            {
                Float fuzzAlpha = std.max<Float>(Clamp(F(fuzzRoughness, 0.5f), 0, 1), 0.01f);
                BxDF fuzzed = StackBxDF.Absorbing(new SGGXSheenBxDF(S(fuzzColor, 1), fuzzAlpha, 1, std.max(scatterDepth, 16)), surface, nothing, maxDepth, nSamples,
                                                  twoSided);
                surface = MixBxDF.Of(surface, fuzzed, wFuzz);
            }
            if (alpha < 1)
            {
                surface = MixBxDF.Of(new OSLTransparentBxDF(), surface, alpha);
            }
            if (reciprocal)
            {
                StackBxDF.SetReciprocal(surface, true);
            }
            return surface;
        }

        public override BSDF GetBSDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx, ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            return new BSDF(ctx.n, ctx.ns, ctx.dpdus, buf.Alloc(GetBxDF(texEval, ctx, ref lambda)));
        }

        public static OpenPBRMaterial Create(TextureParameterDictionary parameters, Image normalMap, FileLoc loc, Allocator alloc)
        {
            OpenPBRMaterial m = new OpenPBRMaterial();
            SpectrumTexture Colour(string name) { return parameters.GetSpectrumTextureOrNull(name, SpectrumType.Albedo, alloc); }
            m.baseWeight = parameters.GetFloatTextureOrNull("base_weight", alloc);
            m.baseColor = Colour("base_color");
            m.baseDiffuseRoughness = parameters.GetFloatTextureOrNull("base_diffuse_roughness", alloc);
            m.baseMetalness = parameters.GetFloatTextureOrNull("base_metalness", alloc);
            m.specularWeight = parameters.GetFloatTextureOrNull("specular_weight", alloc);
            m.specularColor = Colour("specular_color");
            m.specularRoughness = parameters.GetFloatTextureOrNull("specular_roughness", alloc);
            m.specularAnisotropy = parameters.GetFloatTextureOrNull("specular_roughness_anisotropy", alloc);
            m.specularIor = ParametricSpectra.Index(parameters, "specular_ior", alloc);
            if (m.specularIor == null)
            {
                m.specularIor = new ConstantSpectrum(1.5f);
            }
            m.transmissionWeight = parameters.GetFloatTextureOrNull("transmission_weight", alloc);
            m.transmissionColor = Colour("transmission_color");
            m.subsurfaceWeight = parameters.GetFloatTextureOrNull("subsurface_weight", alloc);
            m.subsurfaceColor = Colour("subsurface_color");
            m.subsurfaceAnisotropy = parameters.GetOneFloat("subsurface_scatter_anisotropy", 0f);
            m.subsurfaceDepth = parameters.GetOneFloat("subsurface_depth", 30f);
            m.coatWeight = parameters.GetFloatTextureOrNull("coat_weight", alloc);
            m.coatColor = Colour("coat_color");
            m.coatRoughness = parameters.GetFloatTextureOrNull("coat_roughness", alloc);
            m.coatAnisotropy = parameters.GetFloatTextureOrNull("coat_roughness_anisotropy", alloc);
            m.coatIor = ParametricSpectra.Index(parameters, "coat_ior", alloc);
            if (m.coatIor == null)
            {
                m.coatIor = new ConstantSpectrum(1.6f);
            }
            m.fuzzWeight = parameters.GetFloatTextureOrNull("fuzz_weight", alloc);
            m.fuzzColor = Colour("fuzz_color");
            m.fuzzRoughness = parameters.GetFloatTextureOrNull("fuzz_roughness", alloc);
            m.thinFilmWeight = parameters.GetFloatTextureOrNull("thin_film_weight", alloc);
            m.thinFilmThickness = parameters.GetFloatTextureOrNull("thin_film_thickness", alloc);
            m.thinFilmIor = parameters.GetOneFloat("thin_film_ior", 1.4f);
            m.opacity = parameters.GetFloatTextureOrNull("geometry_opacity", alloc);
            m.thinWalled = parameters.GetOneBool("geometry_thin_walled", false);
            m.metalEta = parameters.GetSpectrumTextureOrNull("metal_eta", SpectrumType.Unbounded, alloc);
            m.metalK = parameters.GetSpectrumTextureOrNull("metal_k", SpectrumType.Unbounded, alloc);
            if ((m.metalEta == null) != (m.metalK == null))
            {
                ErrorExit(loc, "\"metal_eta\" and \"metal_k\" must be given together");
            }
            m.specularHaze = parameters.GetFloatTextureOrNull("specular_haze", alloc);
            m.specularHazeSpread = parameters.GetFloatTextureOrNull("specular_haze_spread", alloc);
            m.specularRetro = Clamp(parameters.GetOneFloat("specular_retroreflectivity", 0f), 0, 1);
            m.specular = OpenPBRInterface.Parse(parameters, "specular_", loc, alloc);
            m.metal = OpenPBRInterface.Parse(parameters, "metal_", loc, alloc);
            m.coat = OpenPBRInterface.Parse(parameters, "coat_", loc, alloc);
            m.scatterDepth = parameters.GetOneInt("scatterdepth", 16);
            m.maxDepth = parameters.GetOneInt("maxdepth", 32);
            m.nSamples = parameters.GetOneInt("nsamples", 1);
            m.reciprocal = parameters.GetOneBool("reciprocal", false);
            string mapping = parameters.GetOneString("roughness_mapping", "openpbr");
            if (mapping == "openpbr")
            {
                m.roughnessMapping = 0;
            }
            else if (mapping == "pbrt")
            {
                m.roughnessMapping = 1;
            }
            else if (mapping == "alpha")
            {
                m.roughnessMapping = 2;
            }
            else
            {
                ErrorExit(loc, "\"roughness_mapping\" must be \"openpbr\", \"pbrt\" or \"alpha\"");
            }
            m.displacement = parameters.GetFloatTextureOrNull("displacement", alloc);
            m.normalMap = normalMap;
            MakeInterior(m, parameters, loc, alloc);
            return m;
        }

        private static Float VanDeHulst(Float x)
        {
            Float s = 4.09712f + 4.20863f * x - std.sqrt(9.59217f + 41.6808f * x + 17.7126f * x * x);
            return Clamp(1 - s * s, 0, 1);
        }

        private static void MakeInterior(OpenPBRMaterial m, TextureParameterDictionary parameters, FileLoc loc, Allocator alloc)
        {
            Float depth = parameters.GetOneFloat("transmission_depth", 0f);
            string mode = parameters.GetOneString("subsurface_mode", "bsdf");
            if (mode != "bsdf" && mode != "volume")
            {
                ErrorExit(loc, "\"subsurface_mode\" must be \"bsdf\" or \"volume\"");
            }
            Float wT = Clamp(parameters.GetOneFloat("transmission_weight", 0f), 0, 1), wS = Clamp(parameters.GetOneFloat("subsurface_weight", 0f), 0, 1);
            bool transmission = depth > 0 && wT > 0 && !m.thinWalled, subsurface = mode == "volume" && wS > 0 && wT < 1 && !m.thinWalled;
            string scattering = parameters.GetOneString("volume_scattering", "multiple");
            if (!transmission && !subsurface)
            {
                return;
            }
            Spectrum white = new ConstantSpectrum(1f), black = new ConstantSpectrum(0f);
            Spectrum colour = parameters.GetOneSpectrum("transmission_color", white, SpectrumType.Albedo, alloc);
            Spectrum scatter = parameters.GetOneSpectrum("transmission_scatter", black, SpectrumType.Albedo, alloc);
            Spectrum skin = parameters.GetOneSpectrum("subsurface_color", new ConstantSpectrum(0.8f), SpectrumType.Albedo, alloc);
            Spectrum scale = parameters.GetOneSpectrum("subsurface_radius_scale", new RGBAlbedoSpectrum(RGBColorSpace.sRGB, new RGB(1f, 0.5f, 0.25f)), SpectrumType.Albedo, alloc);
            Float radius = std.max<Float>(parameters.GetOneFloat("subsurface_radius", 1f), 1e-6f);
            Float gT = Clamp(parameters.GetOneFloat("transmission_scatter_anisotropy", 0f), -0.99f, 0.99f);
            Float gS = Clamp(parameters.GetOneFloat("subsurface_scatter_anisotropy", 0f), -0.99f, 0.99f);
            Float a = transmission ? wT : 0, b = subsurface ? (1 - wT) * wS : 0;
            Float total = a + b;
            a /= total;
            b /= total;
            // (the specification: where the scattering asked for is more than the extinction of the colour, the absorption
            // is raised by the same amount at every wavelength so that it is nowhere negative)
            Float shift = 0;
            if (transmission)
            {
                for (Float lambda = Lambda_min; lambda <= Lambda_max; lambda += 5)
                {
                    Float absorption = -std.log(Clamp(colour.Evaluate(lambda), 1e-4f, 1)) / depth - scatter.Evaluate(lambda) / depth;
                    shift = std.max<Float>(shift, -absorption);
                }
            }
            Func<Float, Float> sigma_a = (Float lambda) =>
            {
                Float value = 0;
                if (transmission)
                {
                    value += a * (-std.log(Clamp(colour.Evaluate(lambda), 1e-4f, 1)) / depth - scatter.Evaluate(lambda) / depth + shift);
                }
                if (subsurface)
                {
                    Float extinction = 1 / std.max<Float>(radius * scale.Evaluate(lambda), 1e-6f);
                    value += b * (1 - VanDeHulst(Clamp(skin.Evaluate(lambda), 0, 1))) * extinction;
                }
                return std.max<Float>(value, 0);
            };
            Func<Float, Float> sigma_s = (Float lambda) =>
            {
                Float value = 0;
                if (transmission)
                {
                    value += a * scatter.Evaluate(lambda) / depth;
                }
                if (subsurface)
                {
                    Float extinction = 1 / std.max<Float>(radius * scale.Evaluate(lambda), 1e-6f);
                    value += b * VanDeHulst(Clamp(skin.Evaluate(lambda), 0, 1)) * extinction;
                }
                return std.max<Float>(value, 0);
            };
            m.SetOwnVolume(MakeHomogeneousVolume(sigma_a, sigma_s, a * gT + b * gS, scattering, loc, alloc));
            m.interiorTransmission = transmission;
            m.subsurfaceVolume = subsurface;
        }
    }
}
