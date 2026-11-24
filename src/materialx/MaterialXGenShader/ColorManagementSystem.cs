// MaterialX is Copyright Contributors to the MaterialX Project and is licensed
// under Apache-2.0. This file is a C# port of MaterialXGenShader/ColorManagementSystem.h and ColorManagementSystem.cpp.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using static MaterialX.Mx;

namespace MaterialX
{
    // ColorSpaceTransform Definition
    // Structure that represents color space transform information
    public class ColorSpaceTransform
    {
        // ColorSpaceTransform Public Methods
        public ColorSpaceTransform(string ss, string ts, TypeDesc t)
        {
            sourceSpace = ss;
            targetSpace = ts;
            type = t;

            if (type != Type.COLOR3 && type != Type.COLOR4)
            {
                throw new ExceptionShaderGenError("Color space transform can only be a color3 or color4.");
            }
        }

        // ColorSpaceTransform Public Members
        public string sourceSpace = "";

        public string targetSpace = "";

        public TypeDesc type;

        // Comparison operator (operator== in the C++)
        public bool isEqual(ColorSpaceTransform other)
        {
            return sourceSpace == other.sourceSpace &&
                   targetSpace == other.targetSpace &&
                   type == other.type;
        }
    }

    // ColorManagementSystem Definition
    // Abstract base class for color management systems
    public abstract class ColorManagementSystem
    {
        // ColorManagementSystem Public Methods
        // Return the ColorManagementSystem name
        public abstract string getName();

        // Load a library of implementations from the provided document,
        // replacing any previously loaded content.
        public virtual void loadLibrary(Document document)
        {
            _document = document;
        }

        // Returns whether this color management system supports a provided transform
        public bool supportsTransform(ColorSpaceTransform transform)
        {
            if (_document == null)
            {
                throw new ExceptionShaderGenError("No library loaded for color management system");
            }
            return getNodeDef(transform) != null;
        }

        // Returns true if the given color space name is one of the no-op color spaces
        // ("none" and "data") reserved by the MaterialX specification, independent of
        // any particular color management system.
        public static bool isReservedNoOpColorSpace(string colorSpace)
        {
            return colorSpace == "none" || colorSpace == "data";
        }

        // Returns true if no color transformation is required between the given source
        // and target color spaces. This is the case when the two names refer to the same
        // color space, or when either name is a no-op color space such as the reserved
        // "none" and "data" names. The base implementation compares the names directly
        // and consults isReservedNoOpColorSpace(). Subclasses may extend this to
        // recognize aliases of the same color space, such as a legacy name and its color
        // interop equivalent, or additional no-op color spaces known to the color
        // management system, and should call the base implementation as well.
        public virtual bool isNoOpTransform(string sourceColorSpace, string targetColorSpace)
        {
            return sourceColorSpace == targetColorSpace ||
                   isReservedNoOpColorSpace(sourceColorSpace) ||
                   isReservedNoOpColorSpace(targetColorSpace);
        }

        // The colorSpace strings should not be shown directly in a user interface. This function
        // converts a colorSpace into a user-facing name. For example, "lin_rec709_scene" becomes
        // "Linear Rec.709 (sRGB)". If the colorSpace is not recognized, it is returned unchanged.
        public virtual string getUserFacingName(string colorSpace)
        {
            return colorSpace;
        }

        // Create a node to use to perform the given color space transformation.
        public ShaderNode createNode(ShaderGraph parent, ColorSpaceTransform transform, string name, GenContext context)
        {
            NodeDef nodeDef = getNodeDef(transform);
            if (nodeDef == null)
            {
                throw new ExceptionShaderGenError("No nodedef found for transform: ('" + transform.sourceSpace + "', '" + transform.targetSpace + "').");
            }

            return ShaderNode.create(parent, name, nodeDef, context);
        }

        // Returns true if the CMS can create a shader node implementation for a locally managed CMS transform
        public virtual bool hasImplementation(string implName)
        {
            return false;
        }

        // Create an CMS node implementation for a locally managed transform
        public virtual ShaderNodeImpl createImplementation(string implName)
        {
            return null;
        }

        // ColorManagementSystem Protected Methods
        // Protected constructor
        protected ColorManagementSystem()
        {
        }

        // Returns a nodedef for a given transform
        protected abstract NodeDef getNodeDef(ColorSpaceTransform transform);

        // ColorManagementSystem Protected Members
        protected Document _document;
    }
}
