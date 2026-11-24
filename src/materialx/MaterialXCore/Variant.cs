// MaterialX is Copyright Contributors to the MaterialX Project and is licensed
// under Apache-2.0. This file is a C# port of MaterialXCore/Variant.h and Variant.cpp.

using System.Collections.Generic;

namespace MaterialX
{
    // Variant Definition
    public class Variant : InterfaceElement
    {
        public Variant(Element parent, string name) : base(parent, CATEGORY, name)
        {
        }

        public const string CATEGORY = "variant";
    }

    // VariantSet Definition
    public class VariantSet : Element
    {
        // VariantSet Public Methods
        public VariantSet(Element parent, string name) : base(parent, CATEGORY, name)
        {
        }

        public Variant addVariant(string name = Mx.EMPTY_STRING)
        {
            return addChild<Variant>(name);
        }

        public Variant getVariant(string name)
        {
            return getChildOfType<Variant>(name);
        }

        public List<Variant> getVariants()
        {
            return getChildrenOfType<Variant>();
        }

        public void removeVariant(string name)
        {
            removeChildOfType<Variant>(name);
        }

        // VariantSet Public Members
        public const string CATEGORY = "variantset";
    }

    // VariantAssign Definition
    public class VariantAssign : Element
    {
        // VariantAssign Public Methods
        public VariantAssign(Element parent, string name) : base(parent, CATEGORY, name)
        {
        }

        public void setVariantSetString(string value)
        {
            setAttribute(VARIANT_SET_ATTRIBUTE, value);
        }

        public bool hasVariantSetString()
        {
            return hasAttribute(VARIANT_SET_ATTRIBUTE);
        }

        public string getVariantSetString()
        {
            return getAttribute(VARIANT_SET_ATTRIBUTE);
        }

        public void setVariantString(string value)
        {
            setAttribute(VARIANT_ATTRIBUTE, value);
        }

        public bool hasVariantString()
        {
            return hasAttribute(VARIANT_ATTRIBUTE);
        }

        public string getVariantString()
        {
            return getAttribute(VARIANT_ATTRIBUTE);
        }

        // VariantAssign Public Members
        public const string CATEGORY = "variantassign";
        public const string VARIANT_SET_ATTRIBUTE = "variantset";
        public const string VARIANT_ATTRIBUTE = "variant";
    }
}
