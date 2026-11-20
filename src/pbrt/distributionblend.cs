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
//   "float transform" [m00 m01 m10 m11]  "float transform1" ...   (opt-in) the microsurface transformation of a component: its
//                      microsurface is mapped by this matrix of the tangent plane (TransformedDistribution; Atanasov et al. 2022).
//                      [s 0 0 s] divides the roughness by s, a rotation turns the anisotropy, [1 k 0 1] shears it.
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
        // The transformation of each component (null: none); transforms[0] is that of the distribution of the material
        public List<Float[]> transforms = new List<Float[]>();
        public bool transformed;

        private static Float[] ParseTransform(TextureParameterDictionary parameters, string name, FileLoc loc)
        {
            List<Float> values = parameters.GetFloatArray(name);
            if (values.Count == 0)
            {
                return null;
            }
            if (values.Count != 4)
            {
                pbrt.ErrorExit(loc, pbrt.StringPrintf("\"%s\" takes the four numbers m00 m01 m10 m11", name));
            }
            Float determinant = values[0] * values[3] - values[1] * values[2];
            if (!(std.abs(determinant) > 1e-8f))
            {
                pbrt.ErrorExit(loc, pbrt.StringPrintf("\"%s\" has no inverse", name));
            }
            return new Float[] { values[0], values[1], values[2], values[3] };
        }

        private static MicrofacetDistribution Transform(MicrofacetDistribution distribution, Float[] m)
        {
            if (m == null)
            {
                return distribution;
            }
            return new TransformedDistribution(distribution, m[0], m[1], m[2], m[3]);
        }

        // Null when the parameters give one distribution only
        public static DistributionBlend Parse(TextureParameterDictionary parameters, string prefix, FileLoc loc, Allocator alloc)
        {
            DistributionBlend blend = new DistributionBlend();
            blend.weights.Add(parameters.GetOneFloat(prefix + "weight", 1f));
            blend.transforms.Add(ParseTransform(parameters, prefix + "transform", loc));
            blend.transformed = blend.transforms[0] != null;
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
                blend.transforms.Add(ParseTransform(parameters, prefix + "transform" + suffix, loc));
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
            return blend.types.Count == 0 && !blend.transformed ? null : blend;
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
            first = Transform(first, blend.transforms[0]);
            if (blend.types.Count == 0)
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
                parts[i] = Transform(MicrofacetDistribution.Create(blend.types[i - 1], ax, ay, blend.gammas[i - 1]), blend.transforms[i]);
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
