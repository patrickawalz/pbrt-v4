// MaterialX is Copyright Contributors to the MaterialX Project and is licensed
// under Apache-2.0. This file is a C# port of MaterialXGenShader/Util.h and Util.cpp.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using static MaterialX.Mx;

namespace MaterialX
{
    // Util Functions
    public static partial class Mx
    {
        private static readonly List<(string, float)> DEFAULT_INPUT_PAIR_LIST = new List<(string, float)>
        {
            ("opacity", 1.0f),
            ("existence", 1.0f),
            ("alpha", 1.0f),
            ("transmission", 0.0f)
        };

        private const string MIX_CATEGORY = "mix";
        private const string MIX_FG_INPUT = "fg";
        private const string MIX_BG_INPUT = "bg";

        private const char TOKEN_PREFIX = '$';

        // Gaussian kernel weights for different kernel sizes.
        public static readonly float[] GAUSSIAN_KERNEL_3 = new float[]
        {
            0.27901f, 0.44198f, 0.27901f // Sigma 1
        };
        public static readonly float[] GAUSSIAN_KERNEL_5 = new float[]
        {
            0.06136f, 0.24477f, 0.38774f, 0.24477f, 0.06136f // Sigma 1
        };
        public static readonly float[] GAUSSIAN_KERNEL_7 = new float[]
        {
            0.00598f, 0.060626f, 0.241843f, 0.383103f, 0.241843f, 0.060626f, 0.00598f // Sigma 1
        };

        // Returns true if the given element is a surface shader with the potential
        // of being transparent. This can be used by HW shader generators to determine
        // if a shader will require transparency handling.
        //
        // Note: This function will check some common cases for how a surface
        // shader can be transparent. It is not covering all possible cases for
        // how transparency can be done and target applications might need to do
        // additional checks to track transparency correctly. For example, custom
        // surface shader nodes implemented in source code will not be tracked by this
        // function and transparency for such nodes must be tracked separately by the
        // target application.
        //
        public static bool isTransparentSurface(Element element, string target = EMPTY_STRING)
        {
            Node node = element.asA<Node>();
            if (node != null)
            {
                // Handle material nodes.
                if (node.getType() == MATERIAL_TYPE_STRING)
                {
                    List<Node> shaderNodes = getShaderNodes(node);
                    if (shaderNodes.Count != 0)
                    {
                        node = shaderNodes[0];
                    }
                }

                // Handle shader nodes.
                if (isTransparentShaderNode(node, null))
                {
                    return true;
                }

                // Handle graph definitions.
                NodeDef nodeDef = node.getNodeDef();
                InterfaceElement impl = nodeDef != null ? nodeDef.getImplementation(target) : null;
                if (impl != null && impl.isA<NodeGraph>())
                {
                    NodeGraph graph = impl.asA<NodeGraph>();

                    List<Output> outputs = graph.getActiveOutputs();
                    if (outputs.Count != 0)
                    {
                        Output output = outputs[0];
                        if (output.getType() == SURFACE_SHADER_TYPE_STRING)
                        {
                            if (isTransparentShaderGraph(output, target, node))
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            else if (element.isA<Output>())
            {
                // Handle output elements.
                Output output = element.asA<Output>();
                Node outputNode = output.getConnectedNode();
                if (outputNode != null)
                {
                    return isTransparentSurface(outputNode, target);
                }
            }

            return false;
        }

        // Maps a value to a four channel color if it is of the appropriate type.
        // Supported types include float, Vector2, Vector3, Vector4,
        // and Color4. If not mapping is possible the color value is
        // set to opaque black.
        public static void mapValueToColor(Value value, ref Color4 color)
        {
            color = new Color4(0.0f, 0.0f, 0.0f, 1.0f);
            if (value == null)
            {
                return;
            }
            if (value.isA<float>())
            {
                color[0] = value.asA<float>();
            }
            else if (value.isA<Color3>())
            {
                Color3 v = value.asA<Color3>();
                color[0] = v[0];
                color[1] = v[1];
                color[2] = v[2];
            }
            else if (value.isA<Color4>())
            {
                color = value.asA<Color4>();
            }
            else if (value.isA<Vector2>())
            {
                Vector2 v = value.asA<Vector2>();
                color[0] = v[0];
                color[1] = v[1];
            }
            else if (value.isA<Vector3>())
            {
                Vector3 v = value.asA<Vector3>();
                color[0] = v[0];
                color[1] = v[1];
                color[2] = v[2];
            }
            else if (value.isA<Vector4>())
            {
                Vector4 v = value.asA<Vector4>();
                color[0] = v[0];
                color[1] = v[1];
                color[2] = v[2];
                color[3] = v[3];
            }
        }

        // Return whether a nodedef requires an implementation
        public static bool requiresImplementation(NodeDef nodeDef)
        {
            if (nodeDef == null)
            {
                return false;
            }
            const string ORGANIZATION_STRING = "organization";
            if (nodeDef.getNodeGroup() == ORGANIZATION_STRING)
            {
                return false;
            }
            const string TYPE_NONE = "none";
            string typeAttribute = nodeDef.getType();
            return typeAttribute.Length != 0 && typeAttribute != TYPE_NONE;
        }

        // Determine if a given element requires shading / lighting for rendering
        public static bool elementRequiresShading(TypedElement element)
        {
            string elementType = element.getType();
            StringSet colorClosures = new StringSet(new string[] {
                "material", "surfaceshader", "volumeshader", "lightshader",
                "BSDF", "EDF", "VDF"
            });
            return colorClosures.Contains(elementType);
        }

        // Find all renderable material nodes in the given document.
        // @param doc Document to examine
        // @return A vector of renderable material nodes.
        public static List<TypedElement> findRenderableMaterialNodes(Document doc)
        {
            List<TypedElement> renderableNodes = new List<TypedElement>();
            foreach (Node materialNode in doc.getMaterialNodes())
            {
                if (getShaderNodes(materialNode).Count != 0)
                {
                    renderableNodes.Add(materialNode);
                }
            }
            return renderableNodes;
        }

        // Find all renderable elements in the given document, including material nodes if present,
        // or graph outputs of renderable types if no material nodes are found.
        // @param doc Document to examine
        // @return A vector of renderable elements
        public static List<TypedElement> findRenderableElements(Document doc)
        {
            List<TypedElement> renderableElements = findRenderableMaterialNodes(doc);
            if (renderableElements.Count == 0)
            {
                // Collect all graph outputs in the content document.
                List<Output> graphOutputs = new List<Output>();
                foreach (NodeGraph graph in doc.getNodeGraphs())
                {
                    if (!graph.belongsToContentDocument())
                    {
                        continue;
                    }
                    foreach (Output output in graph.getOutputs())
                    {
                        graphOutputs.Add(output);
                    }
                }
                foreach (Output output in doc.getOutputs())
                {
                    if (output.belongsToContentDocument())
                    {
                        graphOutputs.Add(output);
                    }
                }

                // Filter out unconnected outputs and unsupported data types.
                StringSet UNSUPPORTED_TYPES = new StringSet
                {
                    BSDF_TYPE_STRING,
                    EDF_TYPE_STRING,
                    VDF_TYPE_STRING,
                    LIGHT_SHADER_TYPE_STRING
                };
                foreach (Output output in graphOutputs)
                {
                    Node node = output.getConnectedNode();
                    if (node != null && !UNSUPPORTED_TYPES.Contains(node.getType()))
                    {
                        renderableElements.Add(output);
                    }
                }
            }
            return renderableElements;
        }

        // Given a node input, return the corresponding input within its matching nodedef.
        // The optional target string can be used to guide the selection of nodedef declarations.
        public static Input getNodeDefInput(Input nodeInput, string target)
        {
            Element parent = nodeInput != null ? nodeInput.getParent() : null;
            Node node = parent != null ? parent.asA<Node>() : null;
            if (node != null)
            {
                NodeDef nodeDef = node.getNodeDef(target);
                if (nodeDef != null)
                {
                    return nodeDef.getActiveInput(nodeInput.getName());
                }
            }

            return null;
        }

        // Perform token substitutions on the given source string, using the given substitution map.
        // Tokens are required to start with '$' and can only consist of alphanumeric characters.
        // The full token name, including '$' and all following alphanumeric character, will be replaced
        // by the corresponding string in the substitution map, if the token exists in the map.
        public static void tokenSubstitution(StringMap substitutions, ref string source)
        {
            string buffer = EMPTY_STRING;
            int pos = 0, len = source.Length;
            while (pos < len)
            {
                int p1 = source.IndexOf(TOKEN_PREFIX, pos);
                if (p1 != -1 && p1 + 1 < len)
                {
                    buffer += source.Substring(pos, p1 - pos);
                    pos = p1 + 1;
                    string token = TOKEN_PREFIX.ToString();
                    while (pos < len && isalnum(source[pos]))
                    {
                        token += source[pos++];
                    }
                    string found;
                    buffer += (substitutions.TryGetValue(token, out found) ? found : token);
                }
                else
                {
                    buffer += source.Substring(pos);
                    break;
                }
            }
            source = buffer;
        }

        // Compute the UDIM coordinates for a set of UDIM identifiers
        // @return List of UDIM coordinates
        public static List<Vector2> getUdimCoordinates(List<string> udimIdentifiers)
        {
            List<Vector2> udimCoordinates = new List<Vector2>();
            if (udimIdentifiers.Count == 0)
            {
                return udimCoordinates;
            }

            foreach (string udimIdentifier in udimIdentifiers)
            {
                if (udimIdentifier.Length == 0)
                {
                    continue;
                }

                int udimVal;
                if (!stoi(udimIdentifier, out udimVal))
                {
                    throw new Exception("Invalid UDIM identifier specified" + udimIdentifier);
                }
                if (udimVal <= 1000 || udimVal >= 2000)
                {
                    throw new Exception("Invalid UDIM identifier specified" + udimIdentifier);
                }

                // Compute UDIM coordinate and add to list to return
                udimVal -= 1000;
                int uVal = udimVal % 10;
                uVal = (uVal == 0) ? 9 : uVal - 1;
                int vVal = (udimVal - uVal - 1) / 10;
                udimCoordinates.Add(new Vector2((float)(uVal), (float)(vVal)));
            }

            return udimCoordinates;
        }

        // Get the UV scale and offset to transform uv coordinates from UDIM uv space to
        // 0..1 space.
        public static void getUdimScaleAndOffset(List<Vector2> udimCoordinates, Vector2 scaleUV, Vector2 offsetUV)
        {
            if (udimCoordinates.Count == 0)
            {
                return;
            }

            // Find range for lower left corner of each tile based on coordinate
            Vector2 minUV = udimCoordinates[0];
            Vector2 maxUV = udimCoordinates[0];
            for (int i = 1; i < udimCoordinates.Count; i++)
            {
                if (udimCoordinates[i][0] < minUV[0])
                {
                    minUV[0] = udimCoordinates[i][0];
                }
                if (udimCoordinates[i][1] < minUV[1])
                {
                    minUV[1] = udimCoordinates[i][1];
                }
                if (udimCoordinates[i][0] > maxUV[0])
                {
                    maxUV[0] = udimCoordinates[i][0];
                }
                if (udimCoordinates[i][1] > maxUV[1])
                {
                    maxUV[1] = udimCoordinates[i][1];
                }
            }
            // Extend to upper right corner of a tile
            maxUV[0] += 1.0f;
            maxUV[1] += 1.0f;

            scaleUV[0] = 1.0f / (maxUV[0] - minUV[0]);
            scaleUV[1] = 1.0f / (maxUV[1] - minUV[1]);
            offsetUV[0] = -minUV[0];
            offsetUV[1] = -minUV[1];
        }

        // Determine whether the given output is directly connected to a node that
        // generates world-space coordinates (e.g. the "normalmap" node).
        // @param output Output to check
        // @return Return the node if found.
        public static Node connectsToWorldSpaceNode(Output output)
        {
            StringSet WORLD_SPACE_NODE_CATEGORIES = new StringSet(new string[] { "normalmap" });
            Node connectedNode = output != null ? output.getConnectedNode() : null;
            if (connectedNode != null && WORLD_SPACE_NODE_CATEGORIES.Contains(connectedNode.getCategory()))
            {
                return connectedNode;
            }
            return null;
        }

        // Returns true if there is are any value elements with a given set of attributes either on the
        // starting node or any graph upsstream of that node.
        // @param output Starting node
        // @param attributes Attributes to test for
        public static bool hasElementAttributes(Output output, List<string> attributes)
        {
            if (output == null || attributes.Count == 0)
            {
                return false;
            }

            for (GraphIterator it = output.traverseGraph().begin(); !it.isEnd(); it.next())
            {
                Element upstreamElem = it.getUpstreamElement();
                Node upstreamNode = upstreamElem != null ? upstreamElem.asA<Node>() : null;
                if (upstreamNode == null)
                {
                    it.setPruneSubgraph(true);
                    continue;
                }
                NodeDef nodeDef = upstreamNode.getNodeDef();
                foreach (ValueElement nodeDefElement in nodeDef.getActiveValueElements())
                {
                    ValueElement testElement = upstreamNode.getActiveValueElement(nodeDefElement.getName());
                    if (testElement == null)
                    {
                        testElement = nodeDefElement;
                    }
                    foreach (string attr in attributes)
                    {
                        if (testElement.hasAttribute(attr))
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        internal static bool isEqual(float v1, float v2)
        {
            float EPSILON = 0.00001f;
            return MathF.Abs(v1 - v2) < EPSILON;
        }

        internal static bool isEqual(Value value, float f)
        {
            if (value.isA<float>() && isEqual(value.asA<float>(), f))
            {
                return true;
            }
            else if (value.isA<Color3>())
            {
                Color3 color = value.asA<Color3>();
                if (isEqual(color[0], f) && isEqual(color[1], f) && isEqual(color[2], f))
                {
                    return true;
                }
            }
            return false;
        }

        // Get corresponding input for an interfacename for a nodegraph.
        // The check is done for any corresponding nodedef first and then for
        // any direct child input of the nodegraph.
        internal static Input getInputInterface(string interfaceName, Node node)
        {
            Input interfaceInput = null;
            Element parent = node.getParent();
            NodeGraph nodeGraph = parent != null ? parent.asA<NodeGraph>() : null;
            if (nodeGraph != null)
            {
                NodeDef nodeDef = nodeGraph.getNodeDef();
                if (nodeDef != null)
                {
                    interfaceInput = nodeDef.getInput(interfaceName);
                }
                else
                {
                    interfaceInput = nodeGraph.getInput(interfaceName);
                }
            }
            return interfaceInput;
        }

        internal static bool hasTransparentInputs(List<(string, float)> opaqueInputList, Node node)
        {
            foreach ((string, float) opaqueInput in opaqueInputList)
            {
                Input interfaceInput = node.getInput(opaqueInput.Item1);
                if (interfaceInput != null)
                {
                    if (interfaceInput.getConnectedNode() != null)
                    {
                        return true;
                    }
                    Value value = interfaceInput.getValue();
                    if (value != null && !isEqual(value, opaqueInput.Item2))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        internal static bool isTransparentShaderNode(Node node, Node interfaceNode)
        {
            if (node == null || node.getType() != SURFACE_SHADER_TYPE_STRING)
            {
                return false;
            }

            if (node.getCategory() == MIX_CATEGORY)
            {
                Input fg = node.getInput(MIX_FG_INPUT);
                Node fgNode = fg != null ? fg.getConnectedNode() : null;
                if (fgNode != null && isTransparentShaderNode(fgNode, null))
                {
                    return true;
                }
                Input bg = node.getInput(MIX_BG_INPUT);
                Node bgNode = bg != null ? bg.getConnectedNode() : null;
                if (bgNode != null && isTransparentShaderNode(bgNode, null))
                {
                    return true;
                }
                return false;
            }

            // Check against nodedef input hints
            List<(string, float)> inputPairList = new List<(string, float)>(DEFAULT_INPUT_PAIR_LIST);
            NodeDef nodeDef = node.getNodeDef();
            StringMap nodeDefList = nodeDef != null ? nodeDef.getInputHints() : new StringMap();
            foreach (KeyValuePair<string, string> item in nodeDefList)
            {
                string inputName = item.Key;
                if (item.Value == Input.TRANSPARENCY_HINT)
                {
                    inputPairList.Add((inputName, 0.0f));
                }
                else if (item.Value == Input.OPACITY_HINT)
                {
                    inputPairList.Add((inputName, 1.0f));
                }
            }

            // Check against the interface if a node is passed in to check against
            List<(string, float)> interfaceNames = new List<(string, float)>();
            if (interfaceNode != null)
            {
                foreach ((string, float) inputPair in inputPairList)
                {
                    Input checkInput = node.getActiveInput(inputPair.Item1);
                    if (checkInput != null)
                    {
                        string interfaceName = checkInput.getInterfaceName();
                        if (interfaceName.Length != 0)
                        {
                            interfaceNames.Add((interfaceName, inputPair.Item2));
                        }
                    }
                }
                if (interfaceNames.Count != 0)
                {
                    if (hasTransparentInputs(interfaceNames, interfaceNode))
                    {
                        return true;
                    }
                }
            }

            // Check against the child input or the corresponding
            // functional nodegraph's interface if the input is mapped
            // via an interface name.
            foreach ((string, float) inputPair in inputPairList)
            {
                Input checkInput = node.getActiveInput(inputPair.Item1);
                if (checkInput != null)
                {
                    string interfaceName = checkInput.getInterfaceName();
                    if (interfaceName.Length != 0)
                    {
                        Input interfaceInput = getInputInterface(interfaceName, node);
                        if (interfaceInput != null)
                        {
                            checkInput = interfaceInput;
                        }
                        else
                        {
                            return false;
                        }
                    }

                    // If mapped but not an operator node then assume transparency.
                    Node inputNode = checkInput.getConnectedNode();
                    if (inputNode != null)
                    {
                        NodeDef inputNodeDef = inputNode.getNodeDef();
                        string nodeGroup = inputNodeDef != null ? inputNodeDef.getAttribute(NodeDef.NODE_GROUP_ATTRIBUTE) : EMPTY_STRING;
                        if (nodeGroup != NodeDef.ADJUSTMENT_NODE_GROUP &&
                            nodeGroup != NodeDef.CHANNEL_NODE_GROUP)
                        {
                            return true;
                        }
                    }
                    else
                    {
                        Value value = checkInput.getValue();
                        if (value != null && !isEqual(value, inputPair.Item2))
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        internal static bool isTransparentShaderGraph(Output output, string target, Node interfaceNode)
        {
            for (GraphIterator it = output.traverseGraph().begin(); !it.isEnd(); it.next())
            {
                Element upstreamElem = it.getUpstreamElement();
                if (upstreamElem == null)
                {
                    continue;
                }

                if (upstreamElem.isA<Node>())
                {
                    // Handle shader nodes.
                    Node node = upstreamElem.asA<Node>();
                    if (isTransparentShaderNode(node, interfaceNode))
                    {
                        return true;
                    }

                    // Handle graph definitions.
                    NodeDef nodeDef = node.getNodeDef();
                    if (nodeDef != null)
                    {
                        if (nodeDef.getType() == BSDF_TYPE_STRING)
                        {
                            InterfaceElement impl = nodeDef.getImplementation(target);
                            if (impl != null && impl.isA<NodeGraph>())
                            {
                                NodeGraph graph = impl.asA<NodeGraph>();
                                List<Output> outputs = graph.getActiveOutputs();
                                if (outputs.Count > 0)
                                {
                                    Output graphOutput = outputs[0];
                                    if (isTransparentShaderGraph(graphOutput, target, node))
                                    {
                                        return true;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return false;
        }

        // These are deprecated wrappers for older versions of the function interfaces in this module.
        public static void findRenderableMaterialNodes(Document doc, List<TypedElement> elements, bool includeReferencedGraphs,
                                                       HashSet<Element> processedSources)
        {
            elements.Clear();
            elements.AddRange(findRenderableMaterialNodes(doc));
        }

        public static void findRenderableElements(Document doc, List<TypedElement> elements, bool includeReferencedGraphs = false)
        {
            elements.Clear();
            elements.AddRange(findRenderableElements(doc));
        }
    }
}
