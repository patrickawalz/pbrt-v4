// MaterialX is Copyright Contributors to the MaterialX Project and is licensed
// under Apache-2.0. This file is a C# port of MaterialXCore/Version.cpp.

using System;
using System.Collections.Generic;
using static MaterialX.Mx;

namespace MaterialX
{
    public partial class Document
    {
        // Document Upgrade Methods
        // Return the NodeDef associated with a legacy ShaderRef element.
        private static NodeDef getShaderNodeDef(Element shaderRef)
        {
            if (shaderRef.hasAttribute(NodeDef.NODE_DEF_ATTRIBUTE))
            {
                string nodeDefString = shaderRef.getAttribute(NodeDef.NODE_DEF_ATTRIBUTE);
                Document doc = shaderRef.getDocument();
                NodeDef child = doc.getNodeDef(shaderRef.getQualifiedName(nodeDefString));
                return child != null ? child : doc.getNodeDef(nodeDefString);
            }
            if (shaderRef.hasAttribute(NodeDef.NODE_ATTRIBUTE))
            {
                string nodeString = shaderRef.getAttribute(NodeDef.NODE_ATTRIBUTE);
                string type = shaderRef.getAttribute(TypedElement.TYPE_ATTRIBUTE);
                string target = shaderRef.getAttribute(InterfaceElement.TARGET_ATTRIBUTE);
                string version = shaderRef.getAttribute(InterfaceElement.VERSION_ATTRIBUTE);
                List<NodeDef> nodeDefs = shaderRef.getDocument().getMatchingNodeDefs(shaderRef.getQualifiedName(nodeString));
                List<NodeDef> secondary = shaderRef.getDocument().getMatchingNodeDefs(nodeString);
                nodeDefs.AddRange(secondary);
                foreach (NodeDef nodeDef in nodeDefs)
                {
                    if (targetStringsMatch(nodeDef.getTarget(), target) &&
                        nodeDef.isVersionCompatible(version) &&
                        (type.Length == 0 || nodeDef.getType() == type))
                    {
                        return nodeDef;
                    }
                }
            }
            return null;
        }

        // Copy an input between nodes, maintaining all existing bindings.
        private static void copyInputWithBindings(Node sourceNode, string sourceInputName, Node destNode, string destInputName)
        {
            Input sourceInput = sourceNode.getInput(sourceInputName);
            if (sourceInput != null)
            {
                Input destInput = destNode.getInput(destInputName);
                if (destInput == null)
                {
                    destInput = destNode.addInput(destInputName, sourceInput.getType());
                }
                destInput.copyContentFrom(sourceInput);
            }
        }

        private static Input removeUnboundInput(Node node, string inputName)
        {
            Input input = node.getInput(inputName);
            if (input != null && !input.hasNodeName() && !input.hasNodeGraphString() &&
                !input.hasOutputString() && !input.hasInterfaceName())
            {
                node.removeInput(inputName);
                input = null;
            }
            return input;
        }

        private static void copyInputOrConnectWorldGeomProp(Node sourceNode, string sourceInputName, Node destNode, string destInputName, GraphElement graph, string geomCategory)
        {
            if (sourceNode.getInput(sourceInputName) != null)
            {
                copyInputWithBindings(sourceNode, sourceInputName, destNode, destInputName);
                return;
            }

            Node geomNode = graph.addNode(geomCategory, graph.createValidChildName("normalmap_" + geomCategory), "vector3");
            geomNode.setInputValue("space", "world");
            destNode.addInput(destInputName, "vector3").setConnectedNode(geomNode);
        }

        public void upgradeVersion()
        {
            (int, int) documentVersion = getVersionIntegers();
            (int, int) expectedVersion = (MATERIALX_MAJOR_VERSION, MATERIALX_MINOR_VERSION);
            if (documentVersion.CompareTo(expectedVersion) >= 0)
            {
                return;
            }
            int majorVersion = documentVersion.Item1;
            int minorVersion = documentVersion.Item2;

            // Upgrade from v1.22 to v1.23
            if (majorVersion == 1 && minorVersion == 22)
            {
                foreach (Element elem in traverseTree())
                {
                    if (elem.getAttribute(TypedElement.TYPE_ATTRIBUTE) == "vector")
                    {
                        elem.setAttribute(TypedElement.TYPE_ATTRIBUTE, getTypeString<Vector3>());
                    }
                }
                minorVersion = 23;
            }

            // Upgrade from v1.23 to v1.24
            if (majorVersion == 1 && minorVersion == 23)
            {
                foreach (Element elem in traverseTree())
                {
                    if (elem.getCategory() == "shader" && elem.hasAttribute("shadername"))
                    {
                        elem.setAttribute(NodeDef.NODE_ATTRIBUTE, elem.getAttribute("shadername"));
                        elem.removeAttribute("shadername");
                    }
                    foreach (Element child in getChildrenOfType<Element>("assign"))
                    {
                        elem.changeChildCategory(child, "materialassign");
                    }
                }
                minorVersion = 24;
            }

            // Upgrade from v1.24 to v1.25
            if (majorVersion == 1 && minorVersion == 24)
            {
                foreach (Element elem in traverseTree())
                {
                    if (elem.isA<Input>() && elem.hasAttribute("graphname"))
                    {
                        elem.setAttribute("opgraph", elem.getAttribute("graphname"));
                        elem.removeAttribute("graphname");
                    }
                }
                minorVersion = 25;
            }

            // Upgrade from v1.25 to v1.26
            if (majorVersion == 1 && minorVersion == 25)
            {
                foreach (Element elem in traverseTree())
                {
                    if (elem.getCategory() == "constant")
                    {
                        Element param = elem.getChild("color");
                        if (param != null)
                        {
                            param.setName("value");
                        }
                    }
                }
                minorVersion = 26;
            }

            // Upgrade from v1.26 to v1.34
            if (majorVersion == 1 && minorVersion == 26)
            {
                // Upgrade elements in place.
                foreach (Element elem in traverseTree())
                {
                    List<Element> origChildren = elem.getChildren();
                    foreach (Element child in origChildren)
                    {
                        if (child.getCategory() == "opgraph")
                        {
                            elem.changeChildCategory(child, "nodegraph");
                        }
                        else if (child.getCategory() == "shader")
                        {
                            NodeDef nodeDef = elem.changeChildCategory(child, "nodedef").asA<NodeDef>();
                            if (nodeDef.hasAttribute("shadertype"))
                            {
                                nodeDef.setType(SURFACE_SHADER_TYPE_STRING);
                            }
                            if (nodeDef.hasAttribute("shaderprogram"))
                            {
                                nodeDef.setNodeString(nodeDef.getAttribute("shaderprogram"));
                            }
                        }
                        else if (child.getCategory() == "shaderref")
                        {
                            if (child.hasAttribute("shadertype"))
                            {
                                child.setAttribute(TypedElement.TYPE_ATTRIBUTE, SURFACE_SHADER_TYPE_STRING);
                                child.removeAttribute("shadertype");
                            }
                        }
                        else if (child.getCategory() == "parameter")
                        {
                            if (child.getAttribute(TypedElement.TYPE_ATTRIBUTE) == "opgraphnode")
                            {
                                if (elem.isA<Node>())
                                {
                                    Input input = elem.changeChildCategory(child, "input").asA<Input>();
                                    input.setNodeName(input.getAttribute("value"));
                                    input.removeAttribute("value");
                                    if (input.getConnectedNode() != null)
                                    {
                                        input.setType(input.getConnectedNode().getType());
                                    }
                                    else
                                    {
                                        input.setType(getTypeString<Color3>());
                                    }
                                }
                                else if (elem.isA<Output>())
                                {
                                    if (child.getName() == "in")
                                    {
                                        elem.setAttribute("nodename", child.getAttribute("value"));
                                    }
                                    elem.removeChild(child.getName());
                                }
                            }
                        }
                    }
                }

                // Assign nodedef names to shaderrefs.
                foreach (Element mat in getChildrenOfType<Element>("material"))
                {
                    foreach (Element shaderRef in mat.getChildrenOfType<Element>("shaderref"))
                    {
                        if (getShaderNodeDef(shaderRef) == null)
                        {
                            NodeDef nodeDef = getNodeDef(shaderRef.getName());
                            if (nodeDef != null)
                            {
                                shaderRef.setAttribute(NodeDef.NODE_DEF_ATTRIBUTE, nodeDef.getName());
                                shaderRef.setAttribute(NodeDef.NODE_ATTRIBUTE, nodeDef.getNodeString());
                            }
                        }
                    }
                }

                // Move connections from nodedef inputs to bindinputs.
                List<Element> materials = getChildrenOfType<Element>("material");
                foreach (NodeDef nodeDef in getNodeDefs())
                {
                    foreach (Input input in nodeDef.getActiveInputs())
                    {
                        if (input.hasAttribute("opgraph") && input.hasAttribute("graphoutput"))
                        {
                            foreach (Element mat in materials)
                            {
                                foreach (Element shaderRef in mat.getChildrenOfType<Element>("shaderref"))
                                {
                                    if (getShaderNodeDef(shaderRef) == nodeDef && shaderRef.getChild(input.getName()) == null)
                                    {
                                        Element bindInput = shaderRef.addChildOfCategory("bindinput", input.getName());
                                        bindInput.setAttribute(TypedElement.TYPE_ATTRIBUTE, input.getType());
                                        bindInput.setAttribute("nodegraph", input.getAttribute("opgraph"));
                                        bindInput.setAttribute("output", input.getAttribute("graphoutput"));
                                    }
                                }
                            }
                            input.removeAttribute("opgraph");
                            input.removeAttribute("graphoutput");
                        }
                    }
                }

                // Combine udim assignments into udim sets.
                foreach (GeomInfo geomInfo in getGeomInfos())
                {
                    foreach (Element child in geomInfo.getChildrenOfType<Element>("geomattr"))
                    {
                        geomInfo.changeChildCategory(child, "geomprop");
                    }
                }
                if (getGeomPropValue("udim") != null && getGeomPropValue("udimset") == null)
                {
                    StringSet udimSet = new StringSet();
                    foreach (GeomInfo geomInfo in getGeomInfos())
                    {
                        foreach (GeomProp geomProp in geomInfo.getGeomProps())
                        {
                            if (geomProp.getName() == "udim")
                            {
                                udimSet.Add(geomProp.getValueString());
                            }
                        }
                    }

                    string udimSetString = EMPTY_STRING;
                    foreach (string udim in udimSet)
                    {
                        if (udimSetString.Length == 0)
                        {
                            udimSetString = udim;
                        }
                        else
                        {
                            udimSetString += ", " + udim;
                        }
                    }

                    GeomInfo udimSetInfo = addGeomInfo();
                    udimSetInfo.setGeomPropValue(UDIM_SET_PROPERTY, udimSetString, getTypeString<List<string>>());
                }

                minorVersion = 34;
            }

            // Upgrade from v1.34 to v1.35
            if (majorVersion == 1 && minorVersion == 34)
            {
                foreach (Element elem in traverseTree())
                {
                    if (elem.getAttribute(TypedElement.TYPE_ATTRIBUTE) == "matrix")
                    {
                        elem.setAttribute(TypedElement.TYPE_ATTRIBUTE, getTypeString<Matrix44>());
                    }
                    if (elem.hasAttribute("default") && !elem.hasAttribute(ValueElement.VALUE_ATTRIBUTE))
                    {
                        elem.setAttribute(ValueElement.VALUE_ATTRIBUTE, elem.getAttribute("default"));
                        elem.removeAttribute("default");
                    }

                    MaterialAssign matAssign = elem.asA<MaterialAssign>();
                    if (matAssign != null)
                    {
                        matAssign.setMaterial(matAssign.getName());
                    }
                }
                minorVersion = 35;
            }

            // Upgrade from v1.35 to v1.36
            if (majorVersion == 1 && minorVersion == 35)
            {
                foreach (Element elem in traverseTree())
                {
                    Look look = elem.asA<Look>();
                    GeomInfo geomInfo = elem.asA<GeomInfo>();

                    if (elem.getAttribute(TypedElement.TYPE_ATTRIBUTE) == GEOMNAME_TYPE_STRING &&
                        elem.getAttribute(ValueElement.VALUE_ATTRIBUTE) == "*")
                    {
                        elem.setAttribute(ValueElement.VALUE_ATTRIBUTE, UNIVERSAL_GEOM_NAME);
                    }
                    if (elem.getAttribute(TypedElement.TYPE_ATTRIBUTE) == FILENAME_TYPE_STRING)
                    {
                        StringMap stringMap = new StringMap();
                        stringMap["%UDIM"] = UDIM_TOKEN;
                        stringMap["%UVTILE"] = UV_TILE_TOKEN;
                        elem.setAttribute(ValueElement.VALUE_ATTRIBUTE, replaceSubstrings(elem.getAttribute(ValueElement.VALUE_ATTRIBUTE), stringMap));
                    }

                    List<Element> origChildren = elem.getChildren();
                    foreach (Element child in origChildren)
                    {
                        if (elem.getCategory() == "material" && child.getCategory() == "override")
                        {
                            foreach (Element shaderRef in elem.getChildrenOfType<Element>("shaderref"))
                            {
                                NodeDef nodeDef = getShaderNodeDef(shaderRef);
                                if (nodeDef != null)
                                {
                                    foreach (ValueElement activeValue in nodeDef.getActiveValueElements())
                                    {
                                        if (activeValue.getAttribute("publicname") == child.getName() &&
                                            shaderRef.getChild(child.getName()) == null)
                                        {
                                            if (activeValue.getCategory() == "parameter")
                                            {
                                                Element bindParam = shaderRef.addChildOfCategory("bindparam", activeValue.getName());
                                                bindParam.setAttribute(TypedElement.TYPE_ATTRIBUTE, activeValue.getType());
                                                bindParam.setAttribute(ValueElement.VALUE_ATTRIBUTE, child.getAttribute("value"));
                                            }
                                            else if (activeValue.isA<Input>())
                                            {
                                                Element bindInput = shaderRef.addChildOfCategory("bindinput", activeValue.getName());
                                                bindInput.setAttribute(TypedElement.TYPE_ATTRIBUTE, activeValue.getType());
                                                bindInput.setAttribute(ValueElement.VALUE_ATTRIBUTE, child.getAttribute("value"));
                                            }
                                        }
                                    }
                                }
                            }
                            elem.removeChild(child.getName());
                        }
                        else if (elem.getCategory() == "material" && child.getCategory() == "materialinherit")
                        {
                            elem.setInheritString(child.getAttribute("material"));
                            elem.removeChild(child.getName());
                        }
                        else if (look != null && child.getCategory() == "lookinherit")
                        {
                            elem.setInheritString(child.getAttribute("look"));
                            elem.removeChild(child.getName());
                        }
                    }
                }
                minorVersion = 36;
            }

            // Upgrade from 1.36 to 1.37
            if (majorVersion == 1 && minorVersion == 36)
            {
                // Convert type attributes to child outputs.
                foreach (NodeDef nodeDef in getNodeDefs())
                {
                    InterfaceElement interfaceElem = nodeDef;
                    if (interfaceElem != null && interfaceElem.hasType())
                    {
                        string type = interfaceElem.getAttribute(TypedElement.TYPE_ATTRIBUTE);
                        Output outputPtr = null;
                        if (type.Length != 0 && type != MULTI_OUTPUT_TYPE_STRING)
                        {
                            outputPtr = interfaceElem.getOutput("out");
                            if (outputPtr == null)
                            {
                                outputPtr = interfaceElem.addOutput("out", type);
                            }
                        }
                        interfaceElem.removeAttribute(TypedElement.TYPE_ATTRIBUTE);

                        string defaultInput = interfaceElem.getAttribute(Output.DEFAULT_INPUT_ATTRIBUTE);
                        if (outputPtr != null && defaultInput.Length != 0)
                        {
                            outputPtr.setAttribute(Output.DEFAULT_INPUT_ATTRIBUTE, defaultInput);
                        }
                        interfaceElem.removeAttribute(Output.DEFAULT_INPUT_ATTRIBUTE);
                    }
                }

                // Remove legacy shader nodedefs.
                foreach (NodeDef nodeDef in getNodeDefs())
                {
                    if (nodeDef.hasAttribute("shadertype"))
                    {
                        foreach (Element mat in getChildrenOfType<Element>("material"))
                        {
                            foreach (Element shaderRef in mat.getChildrenOfType<Element>("shaderref"))
                            {
                                if (shaderRef.getAttribute(InterfaceElement.NODE_DEF_ATTRIBUTE) == nodeDef.getName())
                                {
                                    shaderRef.removeAttribute(InterfaceElement.NODE_DEF_ATTRIBUTE);
                                }
                            }
                        }
                        removeNodeDef(nodeDef.getName());
                    }
                }

                // Convert geometric attributes to geometric properties.
                foreach (GeomInfo geomInfo in getGeomInfos())
                {
                    foreach (Element child in geomInfo.getChildrenOfType<Element>("geomattr"))
                    {
                        geomInfo.changeChildCategory(child, "geomprop");
                    }
                }
                foreach (Element elem in traverseTree())
                {
                    Node node = elem.asA<Node>();
                    if (node == null)
                    {
                        continue;
                    }

                    if (node.getCategory() == "geomattrvalue")
                    {
                        node.setCategory("geompropvalue");
                        if (node.hasAttribute("attrname"))
                        {
                            node.setAttribute("geomprop", node.getAttribute("attrname"));
                            node.removeAttribute("attrname");
                        }
                    }
                }

                List<Node> unusedNodes = new List<Node>();
                foreach (Element elem in traverseTree())
                {
                    Node node = elem.asA<Node>();
                    if (node == null)
                    {
                        continue;
                    }
                    string nodeCategory = node.getCategory();

                    // Change category from "invert to "invertmatrix" for matrix invert nodes
                    if (nodeCategory == "invert" &&
                        (node.getType() == getTypeString<Matrix33>() || node.getType() == getTypeString<Matrix44>()))
                    {
                        node.setCategory("invertmatrix");
                    }

                    // Change category from "rotate" to "rotate2d" or "rotate3d" nodes
                    else if (nodeCategory == "rotate")
                    {
                        node.setCategory((node.getType() == getTypeString<Vector2>()) ? "rotate2d" : "rotate3d");
                    }

                    // Convert "compare" node to "ifgreatereq".
                    else if (nodeCategory == "compare")
                    {
                        node.setCategory("ifgreatereq");
                        Input intest = node.getInput("intest");
                        if (intest != null)
                        {
                            intest.setName("value1");
                        }
                        Element cutoff = node.getChild("cutoff");
                        if (cutoff != null)
                        {
                            cutoff = node.changeChildCategory(cutoff, "input");
                            cutoff.setName("value2");
                        }
                        Input in1 = node.getInput("in1");
                        Input in2 = node.getInput("in2");
                        if (in1 != null && in2 != null)
                        {
                            in1.setName(createValidChildName("temp"));
                            in2.setName("in1");
                            in1.setName("in2");
                        }
                    }

                    // Change nodes with category "transform[vector|point|normal]",
                    // which are not fromspace/tospace variants, to "transformmatrix"
                    else if (nodeCategory == "transformpoint" ||
                             nodeCategory == "transformvector" ||
                             nodeCategory == "transformnormal")
                    {
                        if (node.getChild("fromspace") == null && node.getChild("tospace") == null)
                        {
                            node.setCategory("transformmatrix");
                        }
                    }

                    // Convert "combine" to "combine2", "combine3" or "combine4"
                    else if (nodeCategory == "combine")
                    {
                        if (node.getChild("in4") != null)
                        {
                            node.setCategory("combine4");
                        }
                        else if (node.getChild("in3") != null)
                        {
                            node.setCategory("combine3");
                        }
                        else
                        {
                            node.setCategory("combine2");
                        }
                    }

                    // Convert "separate" to "separate2", "separate3" or "separate4"
                    else if (nodeCategory == "separate")
                    {
                        Input @in = node.getInput("in");
                        if (@in != null)
                        {
                            string inType = @in.getType();
                            if (inType == getTypeString<Vector4>() || inType == getTypeString<Color4>())
                            {
                                node.setCategory("separate4");
                            }
                            else if (inType == getTypeString<Vector3>() || inType == getTypeString<Color3>())
                            {
                                node.setCategory("separate3");
                            }
                            else
                            {
                                node.setCategory("separate2");
                            }
                        }
                    }

                    // Convert backdrop nodes to backdrop elements
                    else if (nodeCategory == "backdrop")
                    {
                        Backdrop backdrop = addBackdrop(node.getName());
                        foreach (Element child in node.getChildrenOfType<Element>("parameter"))
                        {
                            if (child.hasAttribute(ValueElement.VALUE_ATTRIBUTE))
                            {
                                backdrop.setAttribute(child.getName(), child.getAttribute(ValueElement.VALUE_ATTRIBUTE));
                            }
                        }
                        unusedNodes.Add(node);
                    }
                }
                foreach (Node node in unusedNodes)
                {
                    node.getParent().removeChild(node.getName());
                }

                minorVersion = 37;
            }

            // Upgrade from 1.37 to 1.38
            if (majorVersion == 1 && minorVersion == 37)
            {
                // Convert color2 types to vector2
                StringMap COLOR2_CHANNEL_MAP = new StringMap();
                COLOR2_CHANNEL_MAP["r"] = "x";
                COLOR2_CHANNEL_MAP["a"] = "y";
                foreach (Element elem in traverseTree())
                {
                    if (elem.getAttribute(TypedElement.TYPE_ATTRIBUTE) == "color2")
                    {
                        elem.setAttribute(TypedElement.TYPE_ATTRIBUTE, getTypeString<Vector2>());
                        Node parentNode = elem.getParent().asA<Node>();
                        if (parentNode == null)
                        {
                            continue;
                        }

                        foreach (PortElement port in parentNode.getDownstreamPorts())
                        {
                            if (port.hasAttribute("channels"))
                            {
                                string portChannels = port.getAttribute("channels");
                                portChannels = replaceSubstrings(portChannels, COLOR2_CHANNEL_MAP);
                                port.setAttribute("channels", portChannels);
                            }
                            if (port.hasOutputString())
                            {
                                string output = port.getOutputString();
                                output = replaceSubstrings(output, COLOR2_CHANNEL_MAP);
                                port.setOutputString(output);
                            }
                        }

                        Element channels = parentNode.getChild("channels");
                        if (channels != null && channels.hasAttribute(ValueElement.VALUE_ATTRIBUTE))
                        {
                            string value = channels.getAttribute(ValueElement.VALUE_ATTRIBUTE);
                            value = replaceSubstrings(value, COLOR2_CHANNEL_MAP);
                            channels.setAttribute(ValueElement.VALUE_ATTRIBUTE, value);
                        }
                    }
                }

                // Convert material elements to material nodes
                foreach (Element mat in getChildrenOfType<Element>("material"))
                {
                    Node materialNode = null;

                    foreach (Element shaderRef in mat.getChildrenOfType<Element>("shaderref"))
                    {
                        NodeDef nodeDef = getShaderNodeDef(shaderRef);

                        // Get the shader node type and category, using the shader nodedef if present.
                        string shaderNodeType = nodeDef != null ? nodeDef.getType() : SURFACE_SHADER_TYPE_STRING;
                        string shaderNodeCategory = nodeDef != null ? nodeDef.getNodeString() : shaderRef.getAttribute(NodeDef.NODE_ATTRIBUTE);

                        // Add the shader node.
                        string shaderNodeName = createValidChildName(shaderRef.getName());
                        Node shaderNode = addNode(shaderNodeCategory, shaderNodeName, shaderNodeType);

                        // Copy attributes to the shader node.
                        string nodeDefString = shaderRef.getAttribute(NodeDef.NODE_DEF_ATTRIBUTE);
                        string target = shaderRef.getAttribute(InterfaceElement.TARGET_ATTRIBUTE);
                        string version = shaderRef.getAttribute(InterfaceElement.VERSION_ATTRIBUTE);
                        if (nodeDefString.Length != 0)
                        {
                            shaderNode.setNodeDefString(nodeDefString);
                        }
                        if (target.Length != 0)
                        {
                            shaderNode.setTarget(target);
                        }
                        if (version.Length != 0)
                        {
                            shaderNode.setVersionString(version);
                        }
                        shaderNode.setSourceUri(shaderRef.getSourceUri());

                        // Copy child elements to the shader node.
                        foreach (Element child in shaderRef.getChildren())
                        {
                            Element newChild = null;
                            if (child.getCategory() == "bindinput" || child.getCategory() == "bindparam")
                            {
                                newChild = shaderNode.addInput(child.getName());
                            }
                            else if (child.getCategory() == "bindtoken")
                            {
                                newChild = shaderNode.addToken(child.getName());
                            }
                            if (newChild != null)
                            {
                                newChild.copyContentFrom(child);
                            }
                        }

                        // Create a material node if needed, making a connection to the new shader node.
                        if (materialNode == null)
                        {
                            materialNode = addMaterialNode(createValidName("temp"), shaderNode);
                            materialNode.setSourceUri(mat.getSourceUri());
                        }

                        // Assign additional shader inputs to the material as needed.
                        if (materialNode.getInput(shaderNodeType) == null)
                        {
                            Input shaderInput = materialNode.addInput(shaderNodeType, shaderNodeType);
                            shaderInput.setNodeName(shaderNode.getName());
                        }
                    }

                    // Remove the material element, transferring its name and attributes to the material node.
                    removeChild(mat.getName());
                    if (materialNode != null)
                    {
                        materialNode.setName(mat.getName());
                        foreach (string attr in mat.getAttributeNames())
                        {
                            if (!materialNode.hasAttribute(attr))
                            {
                                materialNode.setAttribute(attr, mat.getAttribute(attr));
                            }
                        }
                    }
                }

                // Define BSDF node pairs.
                (string, string) DIELECTRIC_BRDF = ("dielectric_brdf", "dielectric_bsdf");
                (string, string) DIELECTRIC_BTDF = ("dielectric_btdf", "dielectric_bsdf");
                (string, string) GENERALIZED_SCHLICK_BRDF = ("generalized_schlick_brdf", "generalized_schlick_bsdf");
                (string, string) CONDUCTOR_BRDF = ("conductor_brdf", "conductor_bsdf");
                (string, string) SHEEN_BRDF = ("sheen_brdf", "sheen_bsdf");
                (string, string) DIFFUSE_BRDF = ("diffuse_brdf", "oren_nayar_diffuse_bsdf");
                (string, string) BURLEY_DIFFUSE_BRDF = ("burley_diffuse_brdf", "burley_diffuse_bsdf");
                (string, string) DIFFUSE_BTDF = ("diffuse_btdf", "translucent_bsdf");
                (string, string) SUBSURFACE_BRDF = ("subsurface_brdf", "subsurface_bsdf");
                (string, string) THIN_FILM_BRDF = ("thin_film_brdf", "thin_film_bsdf");

                // Function for upgrading old nested layering setup
                // to new setup with layer operators.
                Action<Node> upgradeBsdfLayering = (Node node) =>
                {
                    Input @base = node.getInput("base");
                    if (@base != null)
                    {
                        Node baseNode = @base.getConnectedNode();
                        if (baseNode != null)
                        {
                            GraphElement parent = node.getParent().asA<GraphElement>();
                            // Rename the top bsdf node, and give its old name to the layer operator
                            // so we don't need to update any connection references.
                            string oldName = node.getName();
                            node.setName(oldName + "__layer_top");
                            Node layer = parent.addNode("layer", oldName, "BSDF");
                            Input layerTop = layer.addInput("top", "BSDF");
                            Input layerBase = layer.addInput("base", "BSDF");
                            layerTop.setConnectedNode(node);
                            layerBase.setConnectedNode(baseNode);
                        }
                        node.removeInput("base");
                    }
                };

                // Storage for inputs found connected downstream from artistic_ior node.
                List<Input> artisticIorConnections = new List<Input>();
                List<Input> artisticExtConnections = new List<Input>();

                // Update all nodes.
                foreach (Element elem in traverseTree())
                {
                    Node node = elem.asA<Node>();
                    if (node == null)
                    {
                        continue;
                    }
                    string nodeCategory = node.getCategory();
                    if (nodeCategory == "atan2")
                    {
                        Input input = node.getInput("in1");
                        Input input2 = node.getInput("in2");
                        if (input != null && input2 != null)
                        {
                            input.setName(EMPTY_STRING);
                            input2.setName("in1");
                            input.setName("in2");
                        }
                        else
                        {
                            if (input != null)
                            {
                                input.setName("in2");
                            }
                            if (input2 != null)
                            {
                                input2.setName("in1");
                            }
                        }
                    }
                    else if (nodeCategory == "rotate3d")
                    {
                        Element axis = node.getChild("axis");
                        if (axis != null)
                        {
                            node.changeChildCategory(axis, "input");
                        }
                    }
                    else if (nodeCategory == DIELECTRIC_BRDF.Item1)
                    {
                        node.setCategory(DIELECTRIC_BRDF.Item2);
                        upgradeBsdfLayering(node);
                    }
                    else if (nodeCategory == DIELECTRIC_BTDF.Item1)
                    {
                        node.setCategory(DIELECTRIC_BTDF.Item2);
                        node.removeInput("interior");
                        Input mode = node.addInput("scatter_mode", STRING_TYPE_STRING);
                        mode.setValueString("T");
                    }
                    else if (nodeCategory == GENERALIZED_SCHLICK_BRDF.Item1)
                    {
                        node.setCategory(GENERALIZED_SCHLICK_BRDF.Item2);
                        upgradeBsdfLayering(node);
                    }
                    else if (nodeCategory == SHEEN_BRDF.Item1)
                    {
                        node.setCategory(SHEEN_BRDF.Item2);
                        upgradeBsdfLayering(node);
                    }
                    else if (nodeCategory == THIN_FILM_BRDF.Item1)
                    {
                        node.setCategory(THIN_FILM_BRDF.Item2);
                        upgradeBsdfLayering(node);
                    }
                    else if (nodeCategory == CONDUCTOR_BRDF.Item1)
                    {
                        node.setCategory(CONDUCTOR_BRDF.Item2);

                        // Create an artistic_ior node to convert from artistic to physical parameterization.
                        GraphElement parent = node.getParent().asA<GraphElement>();
                        Node artisticIor = parent.addNode("artistic_ior", node.getName() + "__artistic_ior", "multioutput");
                        Output artisticIor_ior = artisticIor.addOutput("ior", "color3");
                        Output artisticIor_extinction = artisticIor.addOutput("extinction", "color3");

                        // Copy inputs and bindings from conductor node to artistic_ior node.
                        copyInputWithBindings(node, "reflectivity", artisticIor, "reflectivity");
                        copyInputWithBindings(node, "edge_color", artisticIor, "edge_color");

                        // Update the parameterization on the conductor node
                        // and connect it to the artistic_ior node.
                        node.removeInput("reflectivity");
                        node.removeInput("edge_color");
                        Input ior = node.addInput("ior", "color3");
                        ior.setNodeName(artisticIor.getName());
                        ior.setOutputString(artisticIor_ior.getName());
                        Input extinction = node.addInput("extinction", "color3");
                        extinction.setNodeName(artisticIor.getName());
                        extinction.setOutputString(artisticIor_extinction.getName());
                    }
                    else if (nodeCategory == DIFFUSE_BRDF.Item1)
                    {
                        node.setCategory(DIFFUSE_BRDF.Item2);
                    }
                    else if (nodeCategory == BURLEY_DIFFUSE_BRDF.Item1)
                    {
                        node.setCategory(BURLEY_DIFFUSE_BRDF.Item2);
                    }
                    else if (nodeCategory == DIFFUSE_BTDF.Item1)
                    {
                        node.setCategory(DIFFUSE_BTDF.Item2);
                    }
                    else if (nodeCategory == SUBSURFACE_BRDF.Item1)
                    {
                        node.setCategory(SUBSURFACE_BRDF.Item2);
                    }
                    else if (nodeCategory == "artistic_ior")
                    {
                        Output ior = node.getOutput("ior");
                        if (ior != null)
                        {
                            ior.setType("color3");
                        }
                        Output extinction = node.getOutput("extinction");
                        if (extinction != null)
                        {
                            extinction.setType("color3");
                        }
                    }

                    // Search for connections to artistic_ior with vector3 type.
                    // If found we must insert a conversion node color3->vector3
                    // since the outputs of artistic_ior is now color3.
                    // Save the inputs here and insert the conversion nodes below,
                    // since we can't modify the graph while traversing it.
                    foreach (Input input in node.getInputs())
                    {
                        if (input.getOutputString() == "ior" && input.getType() == "vector3")
                        {
                            Node connectedNode = input.getConnectedNode();
                            if (connectedNode != null && connectedNode.getCategory() == "artistic_ior")
                            {
                                artisticIorConnections.Add(input);
                            }
                        }
                        else if (input.getOutputString() == "extinction" && input.getType() == "vector3")
                        {
                            Node connectedNode = input.getConnectedNode();
                            if (connectedNode != null && connectedNode.getCategory() == "artistic_ior")
                            {
                                artisticExtConnections.Add(input);
                            }
                        }
                    }
                }

                // Insert conversion nodes for artistic_ior connections found above.
                foreach (Input input in artisticIorConnections)
                {
                    Node artisticIorNode = input.getConnectedNode();
                    Element node = input.getParent();
                    GraphElement parent = node.getParent().asA<GraphElement>();
                    Node convert = parent.addNode("convert", node.getName() + "__convert_ior", "vector3");
                    Input convertInput = convert.addInput("in", "color3");
                    convertInput.setNodeName(artisticIorNode.getName());
                    convertInput.setOutputString("ior");
                    input.setNodeName(convert.getName());
                    input.removeAttribute(PortElement.OUTPUT_ATTRIBUTE);
                }
                foreach (Input input in artisticExtConnections)
                {
                    Node artisticIorNode = input.getConnectedNode();
                    Element node = input.getParent();
                    GraphElement parent = node.getParent().asA<GraphElement>();
                    Node convert = parent.addNode("convert", node.getName() + "__convert_extinction", "vector3");
                    Input convertInput = convert.addInput("in", "color3");
                    convertInput.setNodeName(artisticIorNode.getName());
                    convertInput.setOutputString("extinction");
                    input.setNodeName(convert.getName());
                    input.removeAttribute(PortElement.OUTPUT_ATTRIBUTE);
                }

                // Make it so that interface names and nodes in a nodegraph are not duplicates
                // If they are, rename the nodes.
                foreach (NodeGraph nodegraph in getNodeGraphs())
                {
                    // Clear out any erroneously set version
                    nodegraph.removeAttribute(InterfaceElement.VERSION_ATTRIBUTE);

                    StringSet interfaceNames = new StringSet();
                    foreach (Element child in new List<Element>(nodegraph.getChildren()))
                    {
                        Node node = child.asA<Node>();
                        if (node != null)
                        {
                            foreach (ValueElement elem in node.getChildrenOfType<ValueElement>())
                            {
                                string interfaceName = elem.getInterfaceName();
                                if (interfaceName.Length != 0)
                                {
                                    interfaceNames.Add(interfaceName);
                                }
                            }
                        }
                    }
                    foreach (string interfaceName in interfaceNames)
                    {
                        Node node = nodegraph.getNode(interfaceName);
                        if (node != null)
                        {
                            string newNodeName = nodegraph.createValidChildName(interfaceName);
                            List<MaterialX.PortElement> downstreamPorts = node.getDownstreamPorts();
                            foreach (MaterialX.PortElement downstreamPort in downstreamPorts)
                            {
                                if (downstreamPort.getNodeName() == interfaceName)
                                {
                                    downstreamPort.setNodeName(newNodeName);
                                }
                            }
                            node.setName(newNodeName);
                        }
                    }
                }

                // Convert parameters to inputs, applying uniform markings to converted inputs
                // of nodedefs.
                foreach (Element elem in traverseTree())
                {
                    if (elem.isA<InterfaceElement>())
                    {
                        foreach (Element param in elem.getChildrenOfType<Element>("parameter"))
                        {
                            Input input = elem.changeChildCategory(param, "input").asA<Input>();
                            if (elem.isA<NodeDef>())
                            {
                                input.setIsUniform(true);
                            }
                        }
                    }
                }

                minorVersion = 38;
            }

            // Upgrade from 1.38 to 1.39
            if (majorVersion == 1 && minorVersion == 38)
            {
                Dictionary<char, int> CHANNEL_INDEX_MAP = new Dictionary<char, int>
                {
                    { 'r', 0 }, { 'g', 1 }, { 'b', 2 }, { 'a', 3 },
                    { 'x', 0 }, { 'y', 1 }, { 'z', 2 }, { 'w', 3 }
                };
                Dictionary<char, float> CHANNEL_CONSTANT_MAP = new Dictionary<char, float>
                {
                    { '0', 0.0f }, { '1', 1.0f }
                };
                Dictionary<string, int> CHANNEL_COUNT_MAP = new Dictionary<string, int>
                {
                    { "float", 1 },
                    { "color3", 3 }, { "color4", 4 },
                    { "vector2", 2 }, { "vector3", 3 }, { "vector4", 4 }
                };
                (string, int)[] CHANNEL_CONVERT_PATTERNS = new (string, int)[]
                {
                    ("rgb", 3), ("rgb", 4), ("rgba", 4),
                    ("xyz", 3), ("xyz", 4), ("xyzw", 4),
                    ("rr", 1), ("rrr", 1),
                    ("xx", 1), ("xxx", 1)
                };
                (StringSet, string)[] CHANNEL_ATTRIBUTE_PATTERNS = new (StringSet, string)[]
                {
                    (new StringSet(new string[] { "xx", "xxx", "xxxx" }), "float"),
                    (new StringSet(new string[] { "xyz", "x", "y", "z" }), "vector3"),
                    (new StringSet(new string[] { "rgba", "a" }), "color4")
                };

                // Convert channels attributes to legacy swizzle nodes, which are then converted
                // to modern nodes in a second pass.
                foreach (Element elem in traverseTree())
                {
                    PortElement port = elem.asA<PortElement>();
                    if (port == null)
                    {
                        continue;
                    }

                    string channelString = port.getAttribute("channels");
                    if (channelString.Length == 0)
                    {
                        continue;
                    }

                    // Determine the upstream type.
                    Element parent = port.getParent();
                    GraphElement graph = port.getAncestorOfType<GraphElement>();
                    Node upstreamNode = port.getConnectedNode();
                    string upstreamType = upstreamNode != null ? upstreamNode.getType() : EMPTY_STRING;
                    if (upstreamType.Length == 0 || upstreamType == MULTI_OUTPUT_TYPE_STRING)
                    {
                        foreach ((StringSet, string) pair in CHANNEL_ATTRIBUTE_PATTERNS)
                        {
                            if (pair.Item1.Contains(channelString))
                            {
                                upstreamType = pair.Item2;
                                break;
                            }
                        }
                        if (upstreamType.Length == 0 || upstreamType == MULTI_OUTPUT_TYPE_STRING)
                        {
                            upstreamType = (port.getType() == "color3") ? "color4" : "color3";
                        }
                    }

                    // Ignore the channels string for purely scalar connections.
                    if (upstreamType == getTypeString<float>() && port.getType() == getTypeString<float>())
                    {
                        port.removeAttribute("channels");
                        continue;
                    }

                    // Create the new swizzle node.
                    Node swizzleNode = graph.addNode("swizzle", graph.createValidChildName("swizzle"), port.getType());
                    int childIndex = (parent.getParent() == graph) ? graph.getChildIndex(parent.getName()) : graph.getChildIndex(port.getName());
                    if (childIndex != -1)
                    {
                        graph.setChildIndex(swizzleNode.getName(), childIndex);
                    }
                    Input @in = swizzleNode.addInput("in");
                    @in.copyContentFrom(port);
                    @in.removeAttribute("channels");
                    @in.setType(upstreamType);
                    swizzleNode.setInputValue("channels", channelString);

                    // Connect the original port to this node.
                    port.setConnectedNode(swizzleNode);
                    port.removeAttribute(PortElement.OUTPUT_ATTRIBUTE);
                    port.removeAttribute(PortElement.INTERFACE_NAME_ATTRIBUTE);
                    port.removeAttribute("channels");

                    // Update any nodegraph reference
                    if (graph != null)
                    {
                        string portNodeGraphString = port.getNodeGraphString();
                        if (portNodeGraphString.Length != 0)
                        {
                            string graphName = graph.getName();
                            if (graphName.Length == 0)
                            {
                                port.removeAttribute(PortElement.NODE_GRAPH_ATTRIBUTE);
                            }
                            else if (graphName != portNodeGraphString)
                            {
                                port.setNodeGraphString(graphName);
                            }
                        }
                    }
                }

                // In MaterialX 1.39, each node input may have only one binding.  Legacy
                // 1.38 nodegraph implementations sometimes kept a default value on an
                // input that was also connected to a nodegraph interface, so preserve the
                // binding and let the declaration provide any default value.
                foreach (Element elem in traverseTree())
                {
                    Input input = elem.asA<Input>();
                    if (input != null && input.getParent().isA<Node>() && input.hasValue() &&
                        (input.hasNodeName() || input.hasNodeGraphString() ||
                         input.hasInterfaceName() || input.hasOutputString()))
                    {
                        input.removeAttribute(ValueElement.VALUE_ATTRIBUTE);
                    }
                }

                // Update all nodes.
                List<Node> unusedNodes = new List<Node>();
                foreach (Element elem in traverseTree())
                {
                    Node node = elem.asA<Node>();
                    if (node == null)
                    {
                        continue;
                    }
                    string nodeCategory = node.getCategory();
                    if (nodeCategory == "layer")
                    {
                        // Convert layering of thin_film_bsdf nodes to thin-film parameters on the affected BSDF nodes.
                        Node top = node.getConnectedNode("top");
                        Node @base = node.getConnectedNode("base");
                        if (top != null && @base != null && top.getCategory() == "thin_film_bsdf")
                        {
                            // Apply thin-film parameters to all supported BSDF's upstream.
                            StringSet BSDF_WITH_THINFILM = new StringSet(new string[] { "dielectric_bsdf", "conductor_bsdf", "generalized_schlick_bsdf" });
                            foreach (Edge edge in node.traverseGraph())
                            {
                                Node upstream = edge.getUpstreamElement().asA<Node>();
                                if (upstream != null && BSDF_WITH_THINFILM.Contains(upstream.getCategory()))
                                {
                                    Input scatterMode = upstream.getInput("scatter_mode");
                                    if (scatterMode == null || scatterMode.getValueString() != "T")
                                    {
                                        copyInputWithBindings(top, "thickness", upstream, "thinfilm_thickness");
                                        copyInputWithBindings(top, "ior", upstream, "thinfilm_ior");
                                    }
                                }
                            }

                            // Bypass the thin-film layer operator.
                            List<MaterialX.PortElement> downstreamPorts = node.getDownstreamPorts();
                            foreach (PortElement port in downstreamPorts)
                            {
                                port.setNodeName(@base.getName());
                            }

                            // Mark original nodes as unused.
                            unusedNodes.Add(node);
                            unusedNodes.Add(top);
                        }
                    }
                    else if (nodeCategory == "subsurface_bsdf")
                    {
                        Input radiusInput = node.getInput("radius");
                        if (radiusInput != null && radiusInput.getType() == "vector3")
                        {
                            GraphElement graph = node.getAncestorOfType<GraphElement>();
                            Node convertNode = graph.addNode("convert", graph.createValidChildName("convert"), "color3");
                            copyInputWithBindings(node, "radius", convertNode, "in");
                            radiusInput.setConnectedNode(convertNode);
                            radiusInput.setType("color3");
                        }
                    }
                    else if (nodeCategory == "switch")
                    {
                        // Upgrade switch nodes from 5 to 10 inputs, handling the fallback behavior for
                        // constant "which" values that were previously out of range.
                        //
                        // getValue() returns null for both a missing and an unparseable value.
                        Input which = node.getInput("which");
                        Value whichValue = which != null ? which.getValue() : null;
                        if (whichValue != null)
                        {
                            if (whichValue.isA<int>() && whichValue.asA<int>() >= 5)
                            {
                                which.setValue(0);
                            }
                            else if (whichValue.isA<float>() && whichValue.asA<float>() >= 5)
                            {
                                which.setValue(0.0);
                            }
                        }
                    }
                    else if (nodeCategory == "swizzle")
                    {
                        Input inInput = node.getInput("in");
                        string sourceType = inInput != null ? inInput.getType() : "float";
                        if (CHANNEL_COUNT_MAP.ContainsKey(sourceType) &&
                            CHANNEL_COUNT_MAP.ContainsKey(node.getType()))
                        {
                            Input channelsInput = node.getInput("channels");
                            string channelString = channelsInput != null ? channelsInput.getValueString() : EMPTY_STRING;
                            string destType = node.getType();
                            int sourceChannelCount = CHANNEL_COUNT_MAP[sourceType];
                            int destChannelCount = CHANNEL_COUNT_MAP[destType];

                            // Resolve the invalid case of having both a connection and a value
                            // by removing the value attribute.
                            if (inInput != null && inInput.hasValue())
                            {
                                if (inInput.hasNodeName() || inInput.hasNodeGraphString() || inInput.hasInterfaceName())
                                {
                                    inInput.removeAttribute(ValueElement.VALUE_ATTRIBUTE);
                                }
                            }

                            // We convert to a constant node if "in" input is a constant value or does not exist:
                            bool convertToConstantNode = inInput == null || inInput.hasValue();
                            // We also convert to a constant node if every destination
                            // channel is constant:
                            //   eg: "ND_swizzle_color3_color3" node with
                            //       "010" in the "channels" input.
                            if (!convertToConstantNode)
                            {
                                convertToConstantNode = true;
                                for (int i = 0; i < destChannelCount; i++)
                                {
                                    if (i < channelString.Length)
                                    {
                                        if (CHANNEL_CONSTANT_MAP.ContainsKey(channelString[i]))
                                        {
                                            // Still in constant territory:
                                            continue;
                                        }
                                    }
                                    // Every other scenario: not constant
                                    convertToConstantNode = false;
                                    break;
                                }
                            }

                            if (convertToConstantNode)
                            {
                                // Replace swizzle with constant.
                                node.setCategory("constant");
                                if (node.hasNodeDefString())
                                {
                                    node.setNodeDefString("ND_constant_" + node.getType());
                                }
                                string valueString = inInput != null ? inInput.getValueString() : "0";
                                List<string> origValueTokens = splitString(valueString, ARRAY_VALID_SEPARATORS);
                                List<string> newValueTokens = new List<string>();
                                for (int i = 0; i < destChannelCount; i++)
                                {
                                    if (i < channelString.Length)
                                    {
                                        if (CHANNEL_INDEX_MAP.ContainsKey(channelString[i]))
                                        {
                                            int index = CHANNEL_INDEX_MAP[channelString[i]];
                                            if (index < origValueTokens.Count)
                                            {
                                                newValueTokens.Add(origValueTokens[index]);
                                                continue;
                                            }
                                        }
                                        else if (CHANNEL_CONSTANT_MAP.ContainsKey(channelString[i]))
                                        {
                                            newValueTokens.Add(Mx.to_string(CHANNEL_CONSTANT_MAP[channelString[i]]));
                                            continue;
                                        }
                                    }
                                    // Invalid channel name, or missing channel name: fall back to the
                                    // first original token, or "0" if the original value was empty.
                                    newValueTokens.Add(origValueTokens.Count == 0 ? "0" : origValueTokens[0]);
                                }
                                Input valueInput = node.addInput("value", node.getType());
                                valueInput.setValueString(joinStrings(newValueTokens, ", "));
                                // This is the last place we need to check for nullptr for inInput.
                                if (inInput != null)
                                {
                                    node.removeInput(inInput.getName());
                                }
                            }
                            else if (destChannelCount == 1)
                            {
                                // Replace swizzle with extract.
                                node.setCategory("extract");
                                if (node.hasNodeDefString())
                                {
                                    node.setNodeDefString("ND_extract_" + sourceType);
                                }
                                if (channelString.Length != 0 && CHANNEL_INDEX_MAP.ContainsKey(channelString[0]))
                                {
                                    node.setInputValue("index", (int) CHANNEL_INDEX_MAP[channelString[0]]);
                                }
                            }
                            else if (sourceType != destType && Array.IndexOf(CHANNEL_CONVERT_PATTERNS,
                                     (channelString, sourceChannelCount)) != -1)
                            {
                                // Replace swizzle with convert.
                                node.setCategory("convert");
                                if (node.hasNodeDefString())
                                {
                                    node.setNodeDefString("ND_convert_" + sourceType + "_" + destType);
                                }
                            }
                            else if (sourceChannelCount == 1)
                            {
                                // Replace swizzle with combine.
                                node.setCategory("combine" + Mx.to_string(destChannelCount));
                                if (node.hasNodeDefString())
                                {
                                    node.setNodeDefString("ND_combine" + Mx.to_string(destChannelCount) + "_" + node.getType());
                                }
                                for (int i = 0; i < destChannelCount; i++)
                                {
                                    Input combineInInput = node.addInput("in" + Mx.to_string(i + 1), "float");
                                    if (i < channelString.Length && CHANNEL_CONSTANT_MAP.ContainsKey(channelString[i]))
                                    {
                                        combineInInput.setValue(CHANNEL_CONSTANT_MAP[channelString[i]]);
                                    }
                                    else
                                    {
                                        copyInputWithBindings(node, inInput.getName(), node, combineInInput.getName());
                                    }
                                }
                                node.removeInput(inInput.getName());
                            }
                            else
                            {
                                // Replace swizzle with separate and combine.
                                GraphElement graph = node.getAncestorOfType<GraphElement>();
                                Node separateNode = graph.addNode("separate" + Mx.to_string(sourceChannelCount),
                                                                      graph.createValidChildName("separate"), MULTI_OUTPUT_TYPE_STRING);
                                int childIndex = graph.getChildIndex(node.getName());
                                if (childIndex != -1)
                                {
                                    graph.setChildIndex(separateNode.getName(), childIndex);
                                }
                                node.setCategory("combine" + Mx.to_string(destChannelCount));
                                if (node.hasNodeDefString())
                                {
                                    node.setNodeDefString("ND_combine" + Mx.to_string(destChannelCount) + "_" + node.getType());
                                }
                                for (int i = 0; i < destChannelCount; i++)
                                {
                                    Input combineInInput = node.addInput("in" + Mx.to_string(i + 1), "float");
                                    if (i < channelString.Length)
                                    {
                                        if (CHANNEL_INDEX_MAP.ContainsKey(channelString[i]))
                                        {
                                            combineInInput.setConnectedNode(separateNode);
                                            combineInInput.setOutputString("out" + channelString[i]);
                                            continue;
                                        }
                                        else if (CHANNEL_CONSTANT_MAP.ContainsKey(channelString[i]))
                                        {
                                            combineInInput.setValue(CHANNEL_CONSTANT_MAP[channelString[i]]);
                                            continue;
                                        }
                                    }
                                    // Invalid channel name, or missing channel name:
                                    combineInInput.setConnectedNode(separateNode);
                                    combineInInput.setOutputString(inInput.isColorType() ? "outr" : "outx");
                                }
                                copyInputWithBindings(node, inInput.getName(), separateNode, "in");
                                node.removeInput(inInput.getName());
                            }

                            // Remove the channels input from the converted node.
                            if (channelsInput != null)
                            {
                                node.removeInput(channelsInput.getName());
                            }
                        }
                    }
                    else if (nodeCategory == "atan2")
                    {
                        Input input1 = node.getInput("in1");
                        if (input1 != null)
                        {
                            input1.setName("iny");
                        }
                        Input input2 = node.getInput("in2");
                        if (input2 != null)
                        {
                            input2.setName("inx");
                        }
                    }
                    else if (nodeCategory == "normalmap")
                    {
                        Input space = node.getInput("space");
                        if (space != null && space.getValueString() == "object")
                        {
                            // Replace object-space normalmap with a graph.
                            GraphElement graph = node.getAncestorOfType<GraphElement>();
                            Node multiply = graph.addNode("multiply", graph.createValidChildName("multiply"), "vector3");
                            copyInputWithBindings(node, "in", multiply, "in1");
                            multiply.setInputValue("in2", 2.0f);
                            Node subtract = graph.addNode("subtract", graph.createValidChildName("subtract"), "vector3");
                            subtract.addInput("in1", "vector3").setConnectedNode(multiply);
                            subtract.setInputValue("in2", 1.0f);
                            node.setCategory("normalize");
                            List<Input> origInputs = node.getInputs();
                            foreach (Input input in origInputs)
                            {
                                node.removeChild(input.getName());
                            }
                            node.addInput("in", "vector3").setConnectedNode(subtract);

                            // Update nodedef name if present.
                            if (node.hasNodeDefString())
                            {
                                node.setNodeDefString("ND_normalize_vector3");
                            }
                        }
                        else
                        {
                            // Clear tangent-space input.
                            node.removeInput("space");

                            // Normalmap frame inputs have 1.39 default geometric properties, so any
                            // unbound legacy literal should be ignored in favor of those geomprops.
                            Input normalInput = removeUnboundInput(node, "normal");
                            Input tangentInput = removeUnboundInput(node, "tangent");
                            Input bitangentInput = removeUnboundInput(node, "bitangent");

                            // If the normal or tangent inputs are set and the bitangent input is not,
                            // the bitangent should be set to normalize(cross(N, T)).
                            if ((normalInput != null || tangentInput != null) && bitangentInput == null)
                            {
                                GraphElement graph = node.getAncestorOfType<GraphElement>();
                                Node crossNode = graph.addNode("crossproduct", graph.createValidChildName("normalmap_cross"), "vector3");
                                copyInputOrConnectWorldGeomProp(node, "normal", crossNode, "in1", graph, "normal");
                                copyInputOrConnectWorldGeomProp(node, "tangent", crossNode, "in2", graph, "tangent");

                                Node normalizeNode = graph.addNode("normalize", graph.createValidChildName("normalmap_cross_norm"), "vector3");
                                normalizeNode.addInput("in", "vector3").setConnectedNode(crossNode);

                                node.addInput("bitangent", "vector3").setConnectedNode(normalizeNode);
                            }

                            // Update nodedef name if present.
                            if (node.getNodeDefString() == "ND_normalmap")
                            {
                                node.setNodeDefString("ND_normalmap_float");
                            }
                        }
                    }
                }
                foreach (Node node in unusedNodes)
                {
                    node.getParent().removeChild(node.getName());
                }

                minorVersion = 39;
            }

            (int, int) upgradedVersion = (majorVersion, minorVersion);
            if (upgradedVersion == expectedVersion)
            {
                setVersionIntegers(majorVersion, minorVersion);
            }
        }
    }
}
