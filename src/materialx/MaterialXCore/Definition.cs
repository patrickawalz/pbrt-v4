// MaterialX is Copyright Contributors to the MaterialX Project and is licensed
// under Apache-2.0. This file is a C# port of MaterialXCore/Definition.h and
// Definition.cpp.

using System.Collections.Generic;
using System.Text;

namespace MaterialX
{
    // Definition Constants
    public static partial class Mx
    {
        public const string COLOR_SEMANTIC = "color";
        public const string SHADER_SEMANTIC = "shader";
    }

    // NodeDef Definition
    public class NodeDef : InterfaceElement
    {
        // NodeDef Public Methods
        public NodeDef(Element parent, string name) : base(parent, CATEGORY, name)
        {
        }

        public void setNodeString(string node)
        {
            setAttribute(NODE_ATTRIBUTE, node);
        }

        public bool hasNodeString()
        {
            return hasAttribute(NODE_ATTRIBUTE);
        }

        public string getNodeString()
        {
            return getAttribute(NODE_ATTRIBUTE);
        }

        public override string getType()
        {
            List<Output> activeOutputs = getActiveOutputs();

            int numActiveOutputs = activeOutputs.Count;
            if (numActiveOutputs > 1)
            {
                return Mx.MULTI_OUTPUT_TYPE_STRING;
            }
            else if (numActiveOutputs == 1)
            {
                return activeOutputs[0].getType();
            }
            else
            {
                return Mx.DEFAULT_TYPE_STRING;
            }
        }

        public void setNodeGroup(string category)
        {
            setAttribute(NODE_GROUP_ATTRIBUTE, category);
        }

        public bool hasNodeGroup()
        {
            return hasAttribute(NODE_GROUP_ATTRIBUTE);
        }

        public string getNodeGroup()
        {
            return getAttribute(NODE_GROUP_ATTRIBUTE);
        }

        public InterfaceElement getImplementation(string target = Mx.EMPTY_STRING, bool resolveNodeGraph = true)
        {
            List<InterfaceElement> interfaces = new List<InterfaceElement>(getDocument().getMatchingImplementations(getQualifiedName(getName())));
            List<InterfaceElement> secondary = getDocument().getMatchingImplementations(getName());
            interfaces.AddRange(secondary);

            if (resolveNodeGraph)
            {
                // Search for the first implementation which matches a given target string.
                for (int i = 0; i < interfaces.Count; ++i)
                {
                    Implementation impl = interfaces[i].asA<Implementation>();
                    if (impl != null && impl.hasNodeGraph())
                    {
                        NodeGraph nodeGraph = getDocument().getNodeGraph(impl.getNodeGraph());
                        if (nodeGraph != null)
                        {
                            interfaces[i] = nodeGraph;
                        }
                    }
                }
            }

            if (target.Length == 0)
            {
                return interfaces.Count != 0 ? interfaces[0] : null;
            }

            // Get all candidate targets matching the given target,
            // taking inheritance into account.
            TargetDef targetDef = getDocument().getTargetDef(target);
            List<string> candidateTargets = targetDef != null ? targetDef.getMatchingTargets() : new List<string>();

            // First, search for a target-specific match.
            foreach (string candidateTarget in candidateTargets)
            {
                foreach (InterfaceElement interfaceElement in interfaces)
                {
                    string interfaceTarget = interfaceElement.getTarget();
                    if (interfaceTarget.Length != 0 && Mx.targetStringsMatch(interfaceTarget, candidateTarget))
                    {
                        return interfaceElement;
                    }
                }
            }

            // Then search for a generic match.
            foreach (InterfaceElement interfaceElement in interfaces)
            {
                string interfaceTarget = interfaceElement.getTarget();
                if (interfaceTarget.Length == 0)
                {
                    return interfaceElement;
                }
            }

            return null;
        }

        public StringMap getInputHints()
        {
            StringMap hints = new StringMap();
            foreach (Input input in getActiveInputs())
            {
                if (input.hasHint())
                {
                    hints[input.getName()] = input.getHint();
                }
            }
            return hints;
        }

        public override bool validate(StringBuilder message = null)
        {
            bool res = true;
            validateRequire(!hasType(), ref res, message, "Nodedef should not have a type but an explicit output");
            return base.validate(message) && res;
        }

        public bool isVersionCompatible(string version)
        {
            if (getVersionString() == version)
            {
                return true;
            }
            if (getDefaultVersion() && version.Length == 0)
            {
                return true;
            }
            return false;
        }

        public override InterfaceElement getDeclaration(string target = Mx.EMPTY_STRING)
        {
            return getSelf().asA<InterfaceElement>();
        }

        // NodeDef Public Members
        public const string CATEGORY = "nodedef";
        public const string NODE_ATTRIBUTE = "node";
        public const string NODE_GROUP_ATTRIBUTE = "nodegroup";

        public const string TEXTURE_NODE_GROUP = "texture";
        public const string PROCEDURAL_NODE_GROUP = "procedural";
        public const string GEOMETRIC_NODE_GROUP = "geometric";
        public const string ADJUSTMENT_NODE_GROUP = "adjustment";
        public const string CONDITIONAL_NODE_GROUP = "conditional";
        public const string CHANNEL_NODE_GROUP = "channel";
        public const string ORGANIZATION_NODE_GROUP = "organization";
        public const string TRANSLATION_NODE_GROUP = "translation";
    }

    // Implementation Definition
    public class Implementation : InterfaceElement
    {
        // Implementation Public Methods
        public Implementation(Element parent, string name) : base(parent, CATEGORY, name)
        {
        }

        public void setFile(string file)
        {
            setAttribute(FILE_ATTRIBUTE, file);
        }

        public bool hasFile()
        {
            return hasAttribute(FILE_ATTRIBUTE);
        }

        public string getFile()
        {
            return getAttribute(FILE_ATTRIBUTE);
        }

        public void setFunction(string function)
        {
            setAttribute(FUNCTION_ATTRIBUTE, function);
        }

        public bool hasFunction()
        {
            return hasAttribute(FUNCTION_ATTRIBUTE);
        }

        public string getFunction()
        {
            return getAttribute(FUNCTION_ATTRIBUTE);
        }

        public void setNodeGraph(string nodegraph)
        {
            setAttribute(NODE_GRAPH_ATTRIBUTE, nodegraph);
        }

        public bool hasNodeGraph()
        {
            return hasAttribute(NODE_GRAPH_ATTRIBUTE);
        }

        public string getNodeGraph()
        {
            return getAttribute(PortElement.NODE_GRAPH_ATTRIBUTE);
        }

        public void setNodeDef(NodeDef nodeDef)
        {
            if (nodeDef != null)
            {
                setNodeDefString(nodeDef.getName());
            }
            else
            {
                removeAttribute(NODE_DEF_ATTRIBUTE);
            }
        }

        public NodeDef getNodeDef()
        {
            return resolveNameReference<NodeDef>(getNodeDefString());
        }

        public override bool validate(StringBuilder message = null)
        {
            bool res = true;
            validateRequire(!hasVersionString(), ref res, message, "Implementation elements do not support version strings");
            return base.validate(message) && res;
        }

        public override InterfaceElement getDeclaration(string target = Mx.EMPTY_STRING)
        {
            return getNodeDef();
        }

        // Implementation Public Members
        public const string CATEGORY = "implementation";
        public const string FILE_ATTRIBUTE = "file";
        public const string FUNCTION_ATTRIBUTE = "function";
        public const string NODE_GRAPH_ATTRIBUTE = "nodegraph";
    }

    // TypeDef Definition
    public class TypeDef : Element
    {
        // TypeDef Public Methods
        public TypeDef(Element parent, string name) : base(parent, CATEGORY, name)
        {
        }

        public void setSemantic(string semantic)
        {
            setAttribute(SEMANTIC_ATTRIBUTE, semantic);
        }

        public bool hasSemantic()
        {
            return hasAttribute(SEMANTIC_ATTRIBUTE);
        }

        public string getSemantic()
        {
            return getAttribute(SEMANTIC_ATTRIBUTE);
        }

        public void setContext(string context)
        {
            setAttribute(CONTEXT_ATTRIBUTE, context);
        }

        public bool hasContext()
        {
            return hasAttribute(CONTEXT_ATTRIBUTE);
        }

        public string getContext()
        {
            return getAttribute(CONTEXT_ATTRIBUTE);
        }

        public Member addMember(string name = Mx.EMPTY_STRING)
        {
            return addChild<Member>(name);
        }

        public Member getMember(string name)
        {
            return getChildOfType<Member>(name);
        }

        public List<Member> getMembers()
        {
            return getChildrenOfType<Member>();
        }

        public void removeMember(string name)
        {
            removeChildOfType<Member>(name);
        }

        // TypeDef Public Members
        public const string CATEGORY = "typedef";
        public const string SEMANTIC_ATTRIBUTE = "semantic";
        public const string CONTEXT_ATTRIBUTE = "context";
    }

    // TargetDef Definition
    public class TargetDef : TypedElement
    {
        // TargetDef Public Methods
        public TargetDef(Element parent, string name) : base(parent, CATEGORY, name)
        {
        }

        public List<string> getMatchingTargets()
        {
            List<string> result = new List<string>();
            result.Add(getName());
            Element baseElement = getInheritsFrom();
            while (baseElement != null)
            {
                result.Add(baseElement.getName());
                baseElement = baseElement.getInheritsFrom();
            }
            return result;
        }

        // TargetDef Public Members
        public const string CATEGORY = "targetdef";
    }

    // Member Definition
    public class Member : ValueElement
    {
        public Member(Element parent, string name) : base(parent, CATEGORY, name)
        {
        }

        public const string CATEGORY = "member";
    }

    // Unit Definition
    public class Unit : Element
    {
        public Unit(Element parent, string name) : base(parent, CATEGORY, name)
        {
        }

        public const string CATEGORY = "unit";
    }

    // UnitDef Definition
    public class UnitDef : Element
    {
        // UnitDef Public Methods
        public UnitDef(Element parent, string name) : base(parent, CATEGORY, name)
        {
        }

        public void setUnitType(string type)
        {
            setAttribute(UNITTYPE_ATTRIBUTE, type);
        }

        public bool hasUnitType()
        {
            return hasAttribute(UNITTYPE_ATTRIBUTE);
        }

        public string getUnitType()
        {
            return getAttribute(UNITTYPE_ATTRIBUTE);
        }

        public Unit addUnit(string name)
        {
            if (name.Length == 0)
            {
                throw new Exception("A unit definition name cannot be empty");
            }
            return addChild<Unit>(name);
        }

        public Unit getUnit(string name)
        {
            return getChildOfType<Unit>(name);
        }

        public List<Unit> getUnits()
        {
            return getChildrenOfType<Unit>();
        }

        public void removeUnit(string name)
        {
            removeChildOfType<Unit>(name);
        }

        // UnitDef Public Members
        public const string CATEGORY = "unitdef";
        public const string UNITTYPE_ATTRIBUTE = "unittype";
    }

    // UnitTypeDef Definition
    public class UnitTypeDef : Element
    {
        // UnitTypeDef Public Methods
        public UnitTypeDef(Element parent, string name) : base(parent, CATEGORY, name)
        {
        }

        public List<UnitDef> getUnitDefs()
        {
            List<UnitDef> unitDefs = new List<UnitDef>();
            foreach (UnitDef unitDef in getDocument().getChildrenOfType<UnitDef>())
            {
                if (unitDef.getUnitType() == _name)
                {
                    unitDefs.Add(unitDef);
                }
            }
            return unitDefs;
        }

        // UnitTypeDef Public Members
        public const string CATEGORY = "unittypedef";
    }

    // AttributeDef Definition
    public class AttributeDef : TypedElement
    {
        // AttributeDef Public Methods
        public AttributeDef(Element parent, string name) : base(parent, CATEGORY, name)
        {
        }

        public void setAttrName(string name)
        {
            setAttribute(ATTRNAME_ATTRIBUTE, name);
        }

        public bool hasAttrName()
        {
            return hasAttribute(ATTRNAME_ATTRIBUTE);
        }

        public string getAttrName()
        {
            return getAttribute(ATTRNAME_ATTRIBUTE);
        }

        public void setValueString(string value)
        {
            setAttribute(VALUE_ATTRIBUTE, value);
        }

        public bool hasValueString()
        {
            return hasAttribute(VALUE_ATTRIBUTE);
        }

        public string getValueString()
        {
            return getAttribute(VALUE_ATTRIBUTE);
        }

        public void setValue<T>(T value, string type = Mx.EMPTY_STRING)
        {
            setType(type.Length != 0 ? type : Mx.getTypeString<T>());
            setValueString(Mx.toValueString<T>(value));
        }

        public bool hasValue()
        {
            return hasAttribute(VALUE_ATTRIBUTE);
        }

        public Value getValue()
        {
            if (!hasValue())
            {
                return null;
            }
            return Value.createValueFromStrings(getValueString(), getType(), getDocument().getTypeDef(getType()));
        }

        public void setElements(string elements)
        {
            setAttribute(ELEMENTS_ATTRIBUTE, elements);
        }

        public bool hasElements()
        {
            return hasAttribute(ELEMENTS_ATTRIBUTE);
        }

        public string getElements()
        {
            return getAttribute(ELEMENTS_ATTRIBUTE);
        }

        public void setExportable(bool value)
        {
            setTypedAttribute<bool>(EXPORTABLE_ATTRIBUTE, value);
        }

        public bool getExportable()
        {
            return getTypedAttribute<bool>(EXPORTABLE_ATTRIBUTE);
        }

        // AttributeDef Public Members
        public const string CATEGORY = "attributedef";
        public const string ATTRNAME_ATTRIBUTE = "attrname";
        public const string VALUE_ATTRIBUTE = "value";
        public const string ELEMENTS_ATTRIBUTE = "elements";
        public const string EXPORTABLE_ATTRIBUTE = "exportable";
    }
}
