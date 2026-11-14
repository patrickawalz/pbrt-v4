// (not in pbrt-v4) Per-material surface-gradient configuration. A material opts in with
//   "string normalmapframework" "surfgrad"
// and then its base normal map, displacement ("bump") texture and an optional second normal map all accumulate into ONE
// surface gradient that is resolved once. That gradient is what the stock path (NormalMap xor BumpMap), the microfacet
// normal map model and the OpenPBR coat normal all consume.

using System;
using System.Collections.Generic;

namespace pbrt
{
    public enum CoatNormalMode : int
    {
        Legacy = 0,       // coat normal map is a tangent normal in the base-perturbed frame
        Inherit = 1,      // coat gradient = base gradient + coat layers (coat follows the base)
        Independent = 2,  // coat gradient = coat layers only (OpenPBR geometry_coat_normal)
    }

    public sealed class SurfgradParams
    {
        public Float nmScale = 1;
        public UVSetTransform nmUV = UVSetTransform.Identity();
        public Image nm2 = null;
        public Float nm2Scale = 1;
        public UVSetTransform nm2UV = UVSetTransform.Identity();
        public TangentNormalBlend blend = TangentNormalBlend.PartialDerivative;
        public Float dispScale = 1;
        public bool dispScaleDependent = true;  // Eq. 7 (world-unit bump height) vs Eq. 17-style TBN
        public UVSetTransform dispUV = UVSetTransform.Identity();
        public CoatNormalMode coatMode = CoatNormalMode.Legacy;
        public Image coatNm = null;
        public Float coatScale = 1;
        public UVSetTransform coatUV = UVSetTransform.Identity();

        // The stacks of the material, built once from its normal map and displacement (they do not change)
        public BumpStack stack = null;
        public BumpStack coatStack = null;
    }

    // (the C++ code keeps a registry from the material's address to its parameters; here the material holds them)
    public abstract unsafe partial class Material
    {
        public SurfgradParams surfgrad = null;

        // Called once the material exists: its normal map and displacement are the first layers
        public void SetSurfgrad(SurfgradParams parameters)
        {
            parameters.stack = new BumpStack();
            pbrt.BuildSurfgradStack(parameters, GetNormalMap(), GetDisplacement(), parameters.stack);
            if (parameters.coatNm != null && parameters.coatMode != CoatNormalMode.Legacy)
            {
                // (the sets of the base stack, so that the two share the per-point data)
                int set = parameters.stack.UVSet(parameters.coatUV);
                parameters.coatStack = new BumpStack();
                for (int i = 0; i < parameters.stack.nUVSets; ++i)
                {
                    parameters.coatStack.uvSets[i] = parameters.stack.uvSets[i];
                }
                parameters.coatStack.nUVSets = parameters.stack.nUVSets;
                BumpLayer layer = parameters.coatStack.Add();
                layer.kind = BumpLayerKind.TangentNormalMap;
                layer.image = parameters.coatNm;
                layer.scale = parameters.coatScale;
                layer.uvSet = set;
            }
            surfgrad = parameters;
        }
    }

    public static unsafe partial class pbrt
    {
        private static readonly Dictionary<string, Image> surfgradNormalMaps = new Dictionary<string, Image>();

        public static Image LoadSurfgradNormalMap(string fn, Allocator alloc)
        {
            if (fn.Length == 0)
            {
                return null;
            }
            lock (surfgradNormalMaps)
            {
                Image cached;
                if (surfgradNormalMaps.TryGetValue(fn, out cached))
                {
                    return cached;
                }
                ImageAndMetadata im = Image.Read(fn, alloc, ColorEncoding.Linear);
                ImageChannelDesc rgb = im.image.GetChannelDesc(new string[] { "R", "G", "B" });
                if (!rgb)
                {
                    ErrorExit(StringPrintf("%s: normal map image must contain R, G, and B channels", fn));
                }
                Image img = im.image.SelectChannels(rgb);
                surfgradNormalMaps[fn] = img;
                return img;
            }
        }

        // The set of texture coordinates of one input: "<prefix>uvchannel" (an integer, or a string for a channel with a
        // name), then "<prefix>uvscale" and "<prefix>rotate" (degrees)
        private static UVSetTransform ParseSurfgradUVSet(ParameterDictionary parameters, string prefix)
        {
            Float tile = parameters.GetOneFloat(prefix + "uvscale", 1f);
            Float rot = parameters.GetOneFloat(prefix + "rotate", 0f);
            UVSetTransform x = UVSetTransform.ScaleRotate(tile, tile, Radians(rot), 0, 0);
            x.channel = parameters.GetOneInt(prefix + "uvchannel", 0);
            string name = parameters.GetOneString(prefix + "uvchannel", "");
            if (name.Length > 0)
            {
                x.channelName = UVChannelNames.Id(name);
            }
            return x;
        }

        // Returns null unless the material asked for the surfgrad framework.
        public static SurfgradParams ParseSurfgradParams(ParameterDictionary parameters, Func<string, string> resolve, Allocator alloc)
        {
            string fw = parameters.GetOneString("normalmapframework", "stock");
            if (fw == "stock")
            {
                return null;
            }
            if (fw != "surfgrad")
            {
                ErrorExit("normalmapframework must be \"stock\" or \"surfgrad\"");
            }
            SurfgradParams p = new SurfgradParams();
            p.nmScale = parameters.GetOneFloat("normalmapscale", 1f);
            p.nmUV = ParseSurfgradUVSet(parameters, "normalmap");
            p.nm2 = LoadSurfgradNormalMap(resolve(parameters.GetOneString("normalmap2", "")), alloc);
            p.nm2Scale = parameters.GetOneFloat("normalmap2scale", 1f);
            p.nm2UV = ParseSurfgradUVSet(parameters, "normalmap2");
            string bl = parameters.GetOneString("normalmapblend", "pd");
            if (bl == "pd")
            {
                p.blend = TangentNormalBlend.PartialDerivative;
            }
            else if (bl == "whiteout")
            {
                p.blend = TangentNormalBlend.Whiteout;
            }
            else if (bl == "udn")
            {
                p.blend = TangentNormalBlend.UDN;
            }
            else if (bl == "rnm")
            {
                p.blend = TangentNormalBlend.Reoriented;
            }
            else
            {
                ErrorExit("normalmapblend must be pd, whiteout, udn or rnm");
            }
            p.dispScale = parameters.GetOneFloat("displacementscale", 1f);
            p.dispScaleDependent = parameters.GetOneString("displacementmode", "bump") == "bump";
            p.dispUV = ParseSurfgradUVSet(parameters, "displacement");
            string cm = parameters.GetOneString("coat.normalmode", "legacy");
            if (cm == "legacy")
            {
                p.coatMode = CoatNormalMode.Legacy;
            }
            else if (cm == "inherit")
            {
                p.coatMode = CoatNormalMode.Inherit;
            }
            else if (cm == "independent")
            {
                p.coatMode = CoatNormalMode.Independent;
            }
            else
            {
                ErrorExit("coat.normalmode must be legacy, inherit or independent");
            }
            p.coatNm = LoadSurfgradNormalMap(resolve(parameters.GetOneString("coat.normalmap", "")), alloc);
            p.coatScale = parameters.GetOneFloat("coat.normalmapscale", 1f);
            p.coatUV = ParseSurfgradUVSet(parameters, "coat.normalmap");
            return p;
        }

        // Builds the base BumpStack for a material from its stock inputs plus the surfgrad extras.
        public static void BuildSurfgradStack(SurfgradParams sp, Image normalMap, FloatTexture displacement, BumpStack stack)
        {
            stack.nLayers = 0;
            stack.nUVSets = 1;
            if (normalMap != null)
            {
                BumpLayer l = stack.Add();
                l.kind = BumpLayerKind.TangentNormalMap;
                l.image = normalMap;
                l.scale = sp.nmScale;
                l.uvSet = stack.UVSet(sp.nmUV);
            }
            if (sp.nm2 != null)
            {
                BumpLayer l = stack.Add();
                l.kind = BumpLayerKind.TangentNormalMap;
                l.image = sp.nm2;
                l.scale = sp.nm2Scale;
                l.uvSet = stack.UVSet(sp.nm2UV);
                l.blend = sp.blend;
            }
            if (displacement != null)
            {
                BumpLayer l = stack.Add();
                l.kind = BumpLayerKind.HeightTexture;
                l.height = displacement;
                l.scale = sp.dispScale;
                l.scaleDependent = sp.dispScaleDependent;
                l.uvSet = stack.UVSet(sp.dispUV);
            }
        }
    }
}
