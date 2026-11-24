// MaterialX is Copyright Contributors to the MaterialX Project and is licensed
// under Apache-2.0. This file is a C# port of MaterialXCore/Look.h and Look.cpp.

using System.Collections.Generic;

namespace MaterialX
{
    // Look Definition
    public class Look : Element
    {
        // Look Public Methods
        public Look(Element parent, string name) : base(parent, CATEGORY, name)
        {
        }

        public MaterialAssign addMaterialAssign(string name = Mx.EMPTY_STRING, string material = Mx.EMPTY_STRING)
        {
            MaterialAssign matAssign = addChild<MaterialAssign>(name);
            if (material.Length != 0)
            {
                matAssign.setMaterial(material);
            }
            return matAssign;
        }

        public MaterialAssign getMaterialAssign(string name)
        {
            return getChildOfType<MaterialAssign>(name);
        }

        public List<MaterialAssign> getMaterialAssigns()
        {
            return getChildrenOfType<MaterialAssign>();
        }

        public List<MaterialAssign> getActiveMaterialAssigns()
        {
            List<MaterialAssign> activeAssigns = new List<MaterialAssign>();
            foreach (Element elem in traverseInheritance())
            {
                List<MaterialAssign> assigns = elem.asA<Look>().getMaterialAssigns();
                activeAssigns.AddRange(assigns);
            }
            return activeAssigns;
        }

        public void removeMaterialAssign(string name)
        {
            removeChildOfType<MaterialAssign>(name);
        }

        public PropertyAssign addPropertyAssign(string name = Mx.EMPTY_STRING)
        {
            return addChild<PropertyAssign>(name);
        }

        public PropertyAssign getPropertyAssign(string name)
        {
            return getChildOfType<PropertyAssign>(name);
        }

        public List<PropertyAssign> getPropertyAssigns()
        {
            return getChildrenOfType<PropertyAssign>();
        }

        public List<PropertyAssign> getActivePropertyAssigns()
        {
            List<PropertyAssign> activeAssigns = new List<PropertyAssign>();
            foreach (Element elem in traverseInheritance())
            {
                List<PropertyAssign> assigns = elem.asA<Look>().getPropertyAssigns();
                activeAssigns.AddRange(assigns);
            }
            return activeAssigns;
        }

        public void removePropertyAssign(string name)
        {
            removeChildOfType<PropertyAssign>(name);
        }

        public PropertySetAssign addPropertySetAssign(string name = Mx.EMPTY_STRING)
        {
            return addChild<PropertySetAssign>(name);
        }

        public PropertySetAssign getPropertySetAssign(string name)
        {
            return getChildOfType<PropertySetAssign>(name);
        }

        public List<PropertySetAssign> getPropertySetAssigns()
        {
            return getChildrenOfType<PropertySetAssign>();
        }

        public List<PropertySetAssign> getActivePropertySetAssigns()
        {
            List<PropertySetAssign> activeAssigns = new List<PropertySetAssign>();
            foreach (Element elem in traverseInheritance())
            {
                List<PropertySetAssign> assigns = elem.asA<Look>().getPropertySetAssigns();
                activeAssigns.AddRange(assigns);
            }
            return activeAssigns;
        }

        public void removePropertySetAssign(string name)
        {
            removeChildOfType<PropertySetAssign>(name);
        }

        public VariantAssign addVariantAssign(string name = Mx.EMPTY_STRING)
        {
            return addChild<VariantAssign>(name);
        }

        public VariantAssign getVariantAssign(string name)
        {
            return getChildOfType<VariantAssign>(name);
        }

        public List<VariantAssign> getVariantAssigns()
        {
            return getChildrenOfType<VariantAssign>();
        }

        public List<VariantAssign> getActiveVariantAssigns()
        {
            List<VariantAssign> activeAssigns = new List<VariantAssign>();
            foreach (Element elem in traverseInheritance())
            {
                List<VariantAssign> assigns = elem.asA<Look>().getVariantAssigns();
                activeAssigns.AddRange(assigns);
            }
            return activeAssigns;
        }

        public void removeVariantAssign(string name)
        {
            removeChildOfType<VariantAssign>(name);
        }

        public Visibility addVisibility(string name = Mx.EMPTY_STRING)
        {
            return addChild<Visibility>(name);
        }

        public Visibility getVisibility(string name)
        {
            return getChildOfType<Visibility>(name);
        }

        public List<Visibility> getVisibilities()
        {
            return getChildrenOfType<Visibility>();
        }

        public List<Visibility> getActiveVisibilities()
        {
            List<Visibility> activeVisibilities = new List<Visibility>();
            foreach (Element elem in traverseInheritance())
            {
                List<Visibility> visibilities = elem.asA<Look>().getVisibilities();
                activeVisibilities.AddRange(visibilities);
            }
            return activeVisibilities;
        }

        public void removeVisibility(string name)
        {
            removeChildOfType<Visibility>(name);
        }

        // Look Public Members
        public const string CATEGORY = "look";
    }

    // LookGroup Definition
    public class LookGroup : Element
    {
        // LookGroup Public Methods
        public LookGroup(Element parent, string name) : base(parent, CATEGORY, name)
        {
        }

        public void setLooks(string looks)
        {
            setAttribute(LOOKS_ATTRIBUTE, looks);
        }

        public string getLooks()
        {
            return getAttribute(LOOKS_ATTRIBUTE);
        }

        public void setActiveLook(string look)
        {
            setAttribute(ACTIVE_ATTRIBUTE, look);
        }

        public string getActiveLook()
        {
            return getAttribute(ACTIVE_ATTRIBUTE);
        }

        // LookGroup Public Members
        public const string CATEGORY = "lookgroup";
        public const string LOOKS_ATTRIBUTE = "looks";
        public const string ACTIVE_ATTRIBUTE = "active";
    }

    // MaterialAssign Definition
    public class MaterialAssign : GeomElement
    {
        // MaterialAssign Public Methods
        public MaterialAssign(Element parent, string name) : base(parent, CATEGORY, name)
        {
        }

        public void setMaterial(string value)
        {
            setAttribute(MATERIAL_ATTRIBUTE, value);
        }

        public bool hasMaterial()
        {
            return hasAttribute(MATERIAL_ATTRIBUTE);
        }

        public string getMaterial()
        {
            return getAttribute(MATERIAL_ATTRIBUTE);
        }

        public List<Output> getMaterialOutputs()
        {
            List<Output> materialOutputs = new List<Output>();
            NodeGraph materialGraph = resolveNameReference<NodeGraph>(getMaterial());
            if (materialGraph != null)
            {
                return materialGraph.getMaterialOutputs();
            }
            return materialOutputs;
        }

        public void setExclusive(bool value)
        {
            setTypedAttribute<bool>(EXCLUSIVE_ATTRIBUTE, value);
        }

        public bool getExclusive()
        {
            return getTypedAttribute<bool>(EXCLUSIVE_ATTRIBUTE);
        }

        public Node getReferencedMaterial()
        {
            return resolveNameReference<Node>(getMaterial());
        }

        public VariantAssign addVariantAssign(string name = Mx.EMPTY_STRING)
        {
            return addChild<VariantAssign>(name);
        }

        public VariantAssign getVariantAssign(string name)
        {
            return getChildOfType<VariantAssign>(name);
        }

        public List<VariantAssign> getVariantAssigns()
        {
            return getChildrenOfType<VariantAssign>();
        }

        public List<VariantAssign> getActiveVariantAssigns()
        {
            List<VariantAssign> activeAssigns = new List<VariantAssign>();
            foreach (Element elem in traverseInheritance())
            {
                List<VariantAssign> assigns = elem.asA<MaterialAssign>().getVariantAssigns();
                activeAssigns.AddRange(assigns);
            }
            return activeAssigns;
        }

        public void removeVariantAssign(string name)
        {
            removeChildOfType<VariantAssign>(name);
        }

        // MaterialAssign Public Members
        public const string CATEGORY = "materialassign";
        public const string MATERIAL_ATTRIBUTE = "material";
        public const string EXCLUSIVE_ATTRIBUTE = "exclusive";
    }

    // Visibility Definition
    public class Visibility : GeomElement
    {
        // Visibility Public Methods
        public Visibility(Element parent, string name) : base(parent, CATEGORY, name)
        {
        }

        public void setViewerGeom(string value)
        {
            setAttribute(VIEWER_GEOM_ATTRIBUTE, value);
        }

        public bool hasViewerGeom()
        {
            return hasAttribute(VIEWER_GEOM_ATTRIBUTE);
        }

        public string getViewerGeom()
        {
            return getAttribute(VIEWER_GEOM_ATTRIBUTE);
        }

        public void setViewerCollection(string value)
        {
            setAttribute(VIEWER_COLLECTION_ATTRIBUTE, value);
        }

        public bool hasViewerCollection()
        {
            return hasAttribute(VIEWER_COLLECTION_ATTRIBUTE);
        }

        public string getViewerCollection()
        {
            return getAttribute(VIEWER_COLLECTION_ATTRIBUTE);
        }

        public void setVisibilityType(string value)
        {
            setAttribute(VISIBILITY_TYPE_ATTRIBUTE, value);
        }

        public bool hasVisibilityType()
        {
            return hasAttribute(VISIBILITY_TYPE_ATTRIBUTE);
        }

        public string getVisibilityType()
        {
            return getAttribute(VISIBILITY_TYPE_ATTRIBUTE);
        }

        public void setVisible(bool visible)
        {
            setTypedAttribute<bool>(VISIBLE_ATTRIBUTE, visible);
        }

        public bool getVisible()
        {
            return getTypedAttribute<bool>(VISIBLE_ATTRIBUTE);
        }

        // Visibility Public Members
        public const string CATEGORY = "visibility";
        public const string VIEWER_GEOM_ATTRIBUTE = "viewergeom";
        public const string VIEWER_COLLECTION_ATTRIBUTE = "viewercollection";
        public const string VISIBILITY_TYPE_ATTRIBUTE = "vistype";
        public const string VISIBLE_ATTRIBUTE = "visible";
    }

    // Look Functions
    public static partial class Mx
    {
        public static List<MaterialAssign> getGeometryBindings(Node materialNode, string geom = UNIVERSAL_GEOM_NAME)
        {
            List<MaterialAssign> matAssigns = new List<MaterialAssign>();
            foreach (Look look in materialNode.getDocument().getLooks())
            {
                foreach (MaterialAssign matAssign in look.getMaterialAssigns())
                {
                    if (matAssign.getReferencedMaterial() == materialNode)
                    {
                        if (geomStringsMatch(matAssign.getActiveGeom(), geom, true))
                        {
                            matAssigns.Add(matAssign);
                            continue;
                        }
                        Collection coll = matAssign.getCollection();
                        if (coll != null && coll.matchesGeomString(geom))
                        {
                            matAssigns.Add(matAssign);
                            continue;
                        }
                    }
                }
            }
            return matAssigns;
        }
    }
}
