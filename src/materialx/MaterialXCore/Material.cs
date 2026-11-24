// MaterialX is Copyright Contributors to the MaterialX Project and is licensed
// under Apache-2.0. This file is a C# port of MaterialXCore/Material.h and
// Material.cpp.

using System;
using System.Collections.Generic;

namespace MaterialX
{
    // Material Functions
    public static partial class Mx
    {
        public static List<Node> getShaderNodes(Node materialNode, string nodeType = SURFACE_SHADER_TYPE_STRING, string target = EMPTY_STRING)
        {
            List<Node> shaderNodeVec = new List<Node>();
            HashSet<Node> shaderNodeSet = new HashSet<Node>(ReferenceEqualityComparer.Instance);

            List<Input> inputs = materialNode.getActiveInputs();
            foreach (Input input in inputs)
            {
                // Scan for a node directly connected to the input.
                // Note that this will handle traversing through interfacename associations.
                Node shaderNode = input.getConnectedNode();
                if (shaderNode != null && !shaderNodeSet.Contains(shaderNode))
                {
                    bool inputMatchesOutputType = nodeType.Length != 0 && shaderNode.getType() == nodeType;

                    // Handle multi-output nodes
                    if (shaderNode.isMultiOutputType())
                    {
                        List<Output> activeOutputs = shaderNode.getActiveOutputs();
                        bool multiOutputMatches = false;
                        foreach (Output output in activeOutputs)
                        {
                            if (output.getType() == nodeType)
                            {
                                multiOutputMatches = true;
                                break;
                            }
                        }
                        inputMatchesOutputType = multiOutputMatches || inputMatchesOutputType;
                    }

                    if (!inputMatchesOutputType)
                    {
                        continue;
                    }

                    if (target.Length != 0)
                    {
                        NodeDef nodeDef = shaderNode.getNodeDef(target);
                        if (nodeDef == null)
                        {
                            continue;
                        }
                    }

                    shaderNodeVec.Add(shaderNode);
                    shaderNodeSet.Add(shaderNode);
                }
                else if (input.hasNodeGraphString())
                {
                    // Check upstream nodegraph connected to the input.
                    // If no explicit output name given then scan all outputs on the nodegraph.
                    Element parent = materialNode.getParent();
                    NodeGraph nodeGraph = parent.getChildOfType<NodeGraph>(input.getNodeGraphString());
                    if (nodeGraph == null)
                    {
                        continue;
                    }
                    List<Output> outputs = new List<Output>();
                    if (input.hasOutputString())
                    {
                        Output connectedOutput = nodeGraph.getOutput(input.getOutputString());
                        if (connectedOutput != null)
                        {
                            outputs.Add(connectedOutput);
                        }
                    }
                    else
                    {
                        outputs = nodeGraph.getOutputs();
                    }
                    foreach (Output output in outputs)
                    {
                        Node upstreamNode = output.getConnectedNode();
                        if (upstreamNode != null && !shaderNodeSet.Contains(upstreamNode))
                        {
                            if (target.Length != 0 && upstreamNode.getNodeDef(target) == null)
                            {
                                continue;
                            }
                            shaderNodeVec.Add(upstreamNode);
                            shaderNodeSet.Add(upstreamNode);
                        }
                    }
                }
            }

            {
                if (shaderNodeVec.Count == 0)
                {
                    // Try to find material nodes in the implementation graph if any.
                    // If a target is specified the nodedef for the given target is searched for.
                    NodeDef materialNodeDef = materialNode.getNodeDef(target);
                    if (materialNodeDef != null)
                    {
                        InterfaceElement impl = materialNodeDef.getImplementation();
                        if (impl != null && impl.isA<NodeGraph>())
                        {
                            NodeGraph implGraph = impl.asA<NodeGraph>();
                            foreach (Output defOutput in materialNodeDef.getOutputs())
                            {
                                if (defOutput.getType() == MATERIAL_TYPE_STRING)
                                {
                                    Output implGraphOutput = implGraph.getOutput(defOutput.getName());
                                    if (implGraphOutput != null)
                                    {
                                        for (GraphIterator it = implGraphOutput.traverseGraph().begin(); !it.isEnd(); it.next())
                                        {
                                            Element upstreamElem = it.getUpstreamElement();
                                            if (upstreamElem == null)
                                            {
                                                it.setPruneSubgraph(true);
                                                continue;
                                            }
                                            Node upstreamNode = upstreamElem.asA<Node>();
                                            if (upstreamNode != null && upstreamNode.getType() == MATERIAL_TYPE_STRING)
                                            {
                                                foreach (Node shaderNode in getShaderNodes(upstreamNode, nodeType, target))
                                                {
                                                    if (!shaderNodeSet.Contains(shaderNode))
                                                    {
                                                        shaderNodeVec.Add(shaderNode);
                                                        shaderNodeSet.Add(shaderNode);
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return shaderNodeVec;
        }

        public static List<Output> getConnectedOutputs(Node node)
        {
            List<Output> outputVec = new List<Output>();
            HashSet<Output> outputSet = new HashSet<Output>(ReferenceEqualityComparer.Instance);
            foreach (Input input in node.getInputs())
            {
                Output output = input.getConnectedOutput();
                if (output != null && !outputSet.Contains(output))
                {
                    outputVec.Add(output);
                    outputSet.Add(output);
                }
            }
            return outputVec;
        }
    }
}
