// (not in pbrt-v4) distributionblend.cs
//
// A distribution of normals made of several: the scene syntax that every material with a "distribution" accepts, and the
// distribution it builds.
//
//   "string distribution"  "float roughness" ...     the first component, as the material reads it
//   "string distribution1" "float roughness1" / "uroughness1" / "vroughness1" (number or float texture), "float gamma1"
//   "string distribution2" ...                       and so on, any number of them (the first index without a distribution ends them)
//   "float weight"  "float weight1" ...              the shares of the components (1; normalised)
//   "string layering"  "mixture" (the default): the components are mixed at every height of the microsurface, one microsurface
//                      whose D, Lambda and projected area are the weighted sums (MixtureDistribution: the blend of Facet Forge,
//                      the "intertwined" mixture of the hazy gloss paper)
//                      "asymmetric": the components are strata of the heights, from the top down, each with the share `weight`
//                      of the heights (LayeredDistribution; d'Eon et al. 2023, "Microfacet theory for non-uniform heightfields")
// A material with several distributions gives a prefix: "interface.distribution1", "conductor.weight", ...

using System;
using System.Collections.Generic;

namespace pbrt
{
    public sealed unsafe class DistributionBlend
    {
        public List<MicrofacetDistributionType> types = new List<MicrofacetDistributionType>();
        public List<Float> gammas = new List<Float>();
        public List<FloatTexture> uRoughness = new List<FloatTexture>(), vRoughness = new List<FloatTexture>();
        // (weights[0] is that of the distribution of the material; the others follow)
        public List<Float> weights = new List<Float>();
        public bool layered;

        // Null when the parameters give one distribution only
        public static DistributionBlend Parse(TextureParameterDictionary parameters, string prefix, FileLoc loc, Allocator alloc)
        {
            DistributionBlend blend = new DistributionBlend();
            blend.weights.Add(parameters.GetOneFloat(prefix + "weight", 1f));
            for (int i = 1; ; ++i)
            {
                string suffix = i.ToString();
                string name = parameters.GetOneString(prefix + "distribution" + suffix, "");
                if (name.Length == 0)
                {
                    break;
                }
                blend.types.Add(MicrofacetDistribution.ParseType(name, loc));
                blend.gammas.Add(parameters.GetOneFloat(prefix + "gamma" + suffix, 2f));
                blend.weights.Add(parameters.GetOneFloat(prefix + "weight" + suffix, 1f));
                FloatTexture u = parameters.GetFloatTextureOrNull(prefix + "uroughness" + suffix, alloc);
                FloatTexture v = parameters.GetFloatTextureOrNull(prefix + "vroughness" + suffix, alloc);
                if (u == null)
                {
                    u = parameters.GetFloatTexture(prefix + "roughness" + suffix, 0.5f, alloc);
                }
                if (v == null)
                {
                    v = parameters.GetFloatTexture(prefix + "roughness" + suffix, 0.5f, alloc);
                }
                blend.uRoughness.Add(u);
                blend.vRoughness.Add(v);
            }
            string layering = parameters.GetOneString(prefix + "layering", "mixture");
            if (layering != "mixture" && layering != "asymmetric")
            {
                pbrt.ErrorExit(loc, "\"layering\" must be \"mixture\" or \"asymmetric\"");
            }
            blend.layered = layering == "asymmetric";
            foreach (Float weight in blend.weights)
            {
                if (!(weight >= 0))
                {
                    pbrt.ErrorExit(loc, "the \"weight\" of a distribution must not be negative");
                }
            }
            return blend.types.Count == 0 ? null : blend;
        }

        // The distribution of the material, `first`, with the others of the blend (first itself when blend is null)
        public static MicrofacetDistribution Create<TextureEvaluator>(DistributionBlend blend, MicrofacetDistribution first, TextureEvaluator texEval,
                                                                     MaterialEvalContext ctx, bool remapRoughness)
            where TextureEvaluator : ITextureEvaluator
        {
            if (blend == null)
            {
                return first;
            }
            int count = blend.types.Count + 1;
            MicrofacetDistribution[] parts = new MicrofacetDistribution[count];
            Float[] shares = new Float[count];
            parts[0] = first;
            shares[0] = blend.weights[0];
            for (int i = 1; i < count; ++i)
            {
                Float ax = texEval.Apply(blend.uRoughness[i - 1], ctx), ay = texEval.Apply(blend.vRoughness[i - 1], ctx);
                if (remapRoughness)
                {
                    ax = TrowbridgeReitzDistribution.RoughnessToAlpha(ax);
                    ay = TrowbridgeReitzDistribution.RoughnessToAlpha(ay);
                }
                parts[i] = MicrofacetDistribution.Create(blend.types[i - 1], ax, ay, blend.gammas[i - 1]);
                shares[i] = blend.weights[i];
            }
            if (blend.layered)
            {
                return new LayeredDistribution(parts, shares);
            }
            return new MixtureDistribution(parts, shares);
        }
    }
}
