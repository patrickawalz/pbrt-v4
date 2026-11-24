// MaterialX is Copyright Contributors to the MaterialX Project and is licensed
// under Apache-2.0. This file is a C# port of MaterialXGenShader/DefaultColorManagementSystem.h and DefaultColorManagementSystem.cpp.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using static MaterialX.Mx;

namespace MaterialX
{
    // DefaultColorManagementSystem Definition
    // Class for a default color management system.
    public class DefaultColorManagementSystem : ColorManagementSystem
    {
        // DefaultColorManagementSystem Public Methods
        // Create a new DefaultColorManagementSystem
        public static DefaultColorManagementSystem create(string target)
        {
            return new DefaultColorManagementSystem(target);
        }

        // Return the DefaultColorManagementSystem name
        public override string getName()
        {
            return CMS_NAME;
        }

        // The colorSpace strings should not be shown directly in a user interface. This function
        // converts a colorSpace into a user-facing name. For example, "lin_rec709_scene" becomes
        // "Linear Rec.709 (sRGB)". If the colorSpace is not recognized, it is returned unchanged.
        public override string getUserFacingName(string colorSpace)
        {
            string baseSpace = remapColorSpace(colorSpace);
            return COLOR_SPACE_USER_FACING_NAMES.ContainsKey(baseSpace) ? COLOR_SPACE_USER_FACING_NAMES[baseSpace] : colorSpace;
        }

        // Returns true if no color transformation is required between the given source
        // and target color spaces. In addition to the base class behavior, this recognizes
        // a legacy color space name and its color interop equivalent as the same color
        // space (e.g. "lin_rec709" and "lin_rec709_scene").
        public override bool isNoOpTransform(string sourceColorSpace, string targetColorSpace)
        {
            return base.isNoOpTransform(sourceColorSpace, targetColorSpace) ||
                   remapColorSpace(sourceColorSpace) == remapColorSpace(targetColorSpace);
        }

        // DefaultColorManagementSystem Protected Methods
        // Returns a nodedef for a given transform
        protected override NodeDef getNodeDef(ColorSpaceTransform transform)
        {
            if (_document == null)
            {
                throw new ExceptionShaderGenError("No library loaded for color management system");
            }

            // A transform that requires no color transformation (e.g. lin_rec709_scene -> lin_rec709)
            // is represented by a pass-through node. ShaderGraph consults isNoOpTransform before
            // requesting a nodedef and omits the transform entirely, so this fallback serves callers
            // of supportsTransform and createNode that request such a transform directly.
            if (isNoOpTransform(transform.sourceSpace, transform.targetSpace))
            {
                return _document.getNodeDef("ND_dot_" + transform.type.getName());
            }

            string sourceSpace = remapColorSpace(transform.sourceSpace);
            string targetSpace = remapColorSpace(transform.targetSpace);
            string nodeName = sourceSpace + "_to_" + targetSpace;

            foreach (NodeDef nodeDef in _document.getMatchingNodeDefs(nodeName))
            {
                foreach (Output output in nodeDef.getOutputs())
                {
                    if (output.getType() == transform.type.getName())
                    {
                        return nodeDef;
                    }
                }
            }
            return null;
        }

        // Protected constructor
        protected DefaultColorManagementSystem(string target)
        {
            _target = target;
        }

        // DefaultColorManagementSystem Private Members
        private string _target = "";

        // DefaultColorManagementSystem Private Methods

        // Return the base color space name for the given color space, resolving any
        // legacy or color interop alias through COLOR_SPACE_REMAP.
        private static string remapColorSpace(string colorSpace)
        {
            string remapped;
            return COLOR_SPACE_REMAP.TryGetValue(colorSpace, out remapped) ? remapped : colorSpace;
        }

        private static StringMap createColorSpaceRemap()
        {
            StringMap map = new StringMap();
            map["gamma18"] = "g18_rec709";
            map["gamma22"] = "g22_rec709";
            map["gamma24"] = "rec709_display";
            map["lin_ap1"] = "acescg";

            // In 1.39 we remap namespaces from ASWF recommended color interop spaces
            //  See https://github.com/AcademySoftwareFoundation/ColorInterop
            // This should improve interop with the nanocolor spaces found in USD.
            map["lin_ap1_scene"] = "acescg";
            map["lin_rec709_scene"] = "lin_rec709";
            map["lin_p3d65_scene"] = "lin_displayp3";
            map["lin_adobergb_scene"] = "lin_adobergb";
            map["srgb_rec709_scene"] = "srgb_texture";
            map["g22_rec709_scene"] = "g22_rec709";
            map["g18_rec709_scene"] = "g18_rec709";
            map["g24_rec709_scene"] = "rec709_display";
            map["g22_ap1_scene"] = "g22_ap1";
            map["srgb_p3d65_scene"] = "srgb_displayp3";
            map["g22_adobergb_scene"] = "adobergb";
            return map;
        }

        private static StringMap createColorSpaceUserFacingNames()
        {
            StringMap map = new StringMap();
            // Base names that COLOR_SPACE_REMAP normalizes onto.
            map["g18_rec709"] = "Gamma 1.8 Encoded Rec.709";
            map["g22_rec709"] = "Gamma 2.2 Encoded Rec.709";
            map["rec709_display"] = "Gamma 2.4 Encoded Rec.709";
            map["acescg"] = "ACEScg";
            map["lin_rec709"] = "Linear Rec.709 (sRGB)";
            map["lin_displayp3"] = "Linear P3-D65";
            map["lin_adobergb"] = "Linear AdobeRGB";
            map["srgb_texture"] = "sRGB Encoded Rec.709 (sRGB)";
            map["g22_ap1"] = "Gamma 2.2 Encoded AP1";
            map["srgb_displayp3"] = "sRGB Encoded P3-D65";
            map["adobergb"] = "Gamma 2.2 Encoded AdobeRGB";

            // Color interop names with no legacy equivalent.
            map["lin_ap0_scene"] = "ACES2065-1";
            map["lin_rec2020_scene"] = "Linear Rec.2020";
            map["lin_ciexyzd65_scene"] = "CIE XYZ-D65 - Scene-referred";
            map["srgb_ap1_scene"] = "sRGB Encoded AP1";

            // No-op color spaces.
            map["none"] = "Data"; // prefer the new name
            map["data"] = "Data";
            return map;
        }

        // DefaultColorManagementSystem Private Members
        private const string CMS_NAME = "default_cms";

        // Remap from legacy color space names to their ACES 1.2 equivalents.
        private static readonly StringMap COLOR_SPACE_REMAP = createColorSpaceRemap();

        // User-facing display names for color space IDs, as tabulated in the color space table
        // of MaterialX.Specification.md. Names handled by COLOR_SPACE_REMAP are looked up here
        // by their remapped (base) name, so each display name is only listed once.
        private static readonly StringMap COLOR_SPACE_USER_FACING_NAMES = createColorSpaceUserFacingNames();
    }
}
