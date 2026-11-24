// MaterialX is Copyright Contributors to the MaterialX Project and is licensed
// under Apache-2.0. This file is a C# port of MaterialXGenShader/ShaderTranslator.h and ShaderTranslator.cpp.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using static MaterialX.Mx;

namespace MaterialX
{
    // ShaderTranslator Definition
    // A helper class for translating content between shading models.
    public class ShaderTranslator
    {
        // ShaderTranslator Public Methods
        public static ShaderTranslator create()
        {
            return new ShaderTranslator();
        }

        // Translate a shader node to the destination shading model.
        public void translateShader(Node shader, string destCategory)
        {
            _graph = null;
            _translationNode = null;

            if (shader == null)
            {
                return;
            }

            string sourceCategory = shader.getCategory();
            if (sourceCategory == destCategory)
            {
                throw new Exception("The source shader \"" + shader.getNamePath() + "\" category is already \"" + destCategory + "\"");
            }

            Document doc = shader.getDocument();
            List<Output> referencedOutputs = getConnectedOutputs(shader);
            Element referencedParent = referencedOutputs.Count != 0 ? referencedOutputs[0].getParent() : null;
            NodeGraph referencedGraph = referencedParent != null ? referencedParent.asA<NodeGraph>() : null;
            _graph = referencedGraph != null ? referencedGraph : doc.addNodeGraph();

            string translateNodeString = sourceCategory + "_to_" + destCategory;
            List<NodeDef> matchingNodeDefs = doc.getMatchingNodeDefs(translateNodeString);
            if (matchingNodeDefs.Count == 0)
            {
                throw new Exception("Shader translation requires a translation nodedef named " + translateNodeString);
            }
            NodeDef translationNodeDef = matchingNodeDefs[0];
            _translationNode = _graph.addNodeInstance(translationNodeDef);

            connectTranslationInputs(shader, translationNodeDef);
            shader.setCategory(destCategory);
            shader.removeAttribute(InterfaceElement.NODE_DEF_ATTRIBUTE);
            connectTranslationOutputs(shader);
        }

        // Translate each material in the input document to the destination
        // shading model.
        public void translateAllMaterials(Document doc, string destCategory)
        {
            List<TypedElement> materialNodes = findRenderableMaterialNodes(doc);
            foreach (TypedElement elem in materialNodes)
            {
                Node materialNode = elem.asA<Node>();
                if (materialNode == null)
                {
                    continue;
                }
                foreach (Node shaderNode in getShaderNodes(materialNode))
                {
                    translateShader(shaderNode, destCategory);
                }
            }
        }

        // ShaderTranslator Protected Methods
        protected ShaderTranslator()
        {
        }

        // Connect translation node inputs from the original shader
        protected void connectTranslationInputs(Node shader, NodeDef translationNodeDef)
        {
            List<Input> origInputs = shader.getInputs();
            HashSet<Output> origOutputs = new HashSet<Output>(ReferenceEqualityComparer.Instance);
            foreach (Input shaderInput in origInputs)
            {
                if (translationNodeDef.getActiveInput(shaderInput.getName()) != null)
                {
                    Input input = _translationNode.addInput(shaderInput.getName(), shaderInput.getType());

                    Output connectedOutput = shaderInput.getConnectedOutput();
                    if (connectedOutput != null)
                    {
                        Node connectedNode = connectedOutput.getConnectedNode();

                        // Nodes with world-space outputs are skipped, with translation being applied to
                        // the node directly upstream.
                        Node worldSpaceNode = connectsToWorldSpaceNode(connectedOutput);
                        if (worldSpaceNode != null)
                        {
                            Node upstreamNode = worldSpaceNode.getConnectedNode("in");
                            if (upstreamNode != null)
                            {
                                connectedNode = upstreamNode;
                            }
                        }

                        input.setConnectedNode(connectedNode);
                        origOutputs.Add(connectedOutput);
                    }
                    else if (shaderInput.hasValueString())
                    {
                        input.setValueString(shaderInput.getValueString());
                    }
                    else
                    {
                        throw new Exception("Shader input has no associated output or value " + shaderInput.getName());
                    }

                    string colorSpace = shaderInput.getActiveColorSpace();
                    if (colorSpace.Length != 0)
                    {
                        input.setColorSpace(colorSpace);
                    }
                    if (shaderInput.hasUnit())
                    {
                        input.setUnit(shaderInput.getUnit());
                        input.setUnitType(shaderInput.getUnitType());
                    }
                }
            }

            foreach (Input input in origInputs)
            {
                shader.removeInput(input.getName());
            }
            foreach (Output output in origOutputs)
            {
                _graph.removeOutput(output.getName());
            }
        }

        // Connect translation node outputs to finalize shader translation
        protected void connectTranslationOutputs(Node shader)
        {
            Document doc = shader.getDocument();
            NodeDef nodeDef = _translationNode.getNodeDef();
            if (nodeDef == null)
            {
                throw new Exception("No nodedef for " + _translationNode.getCategory() + " was found");
            }

            // Iterate through outputs of the translation graph.
            foreach (Output translationGraphOutput in nodeDef.getActiveOutputs())
            {
                // Convert output name to input name, using a hardcoded naming convention for now.
                string outputName = translationGraphOutput.getName();
                int pos = outputName.IndexOf("_out", StringComparison.Ordinal);
                if (pos == -1)
                {
                    throw new Exception("Translation graph output " + outputName + " does not end with '_out'");
                }
                string inputName = outputName.Substring(0, pos);

                // Determine the node and output representing this translated stream.
                Node translatedStreamNode = _translationNode;
                string translatedStreamOutput = outputName;

                // Nodes with world-space outputs are moved outside of their containing graph,
                // providing greater flexibility in texture baking.
                Node worldSpaceNode = connectsToWorldSpaceNode(translationGraphOutput);
                if (worldSpaceNode != null)
                {
                    Input nodeInput = worldSpaceNode.getInput("in");
                    if (nodeInput != null && nodeInput.hasInterfaceName())
                    {
                        Input interfaceInput = _translationNode.getInput(nodeInput.getInterfaceName());
                        Node sourceNode = interfaceInput != null ? interfaceInput.getConnectedNode() : null;
                        if (sourceNode == null)
                        {
                            continue;
                        }
                        translatedStreamNode = _graph.addNode(worldSpaceNode.getCategory(), worldSpaceNode.getName(), worldSpaceNode.getType());
                        translatedStreamNode.setConnectedNode("in", sourceNode);
                        translatedStreamOutput = EMPTY_STRING;
                    }
                }

                // Create translated output.
                Output translatedOutput = _graph.getOutput(outputName);
                if (translatedOutput == null)
                {
                    translatedOutput = _graph.addOutput(outputName, translationGraphOutput.getType());
                }
                translatedOutput.setConnectedNode(translatedStreamNode);
                if (translatedStreamOutput.Length != 0)
                {
                    translatedOutput.setOutputString(translatedStreamOutput);
                }

                // Add translated shader input.
                Input translatedShaderInput = shader.getInput(inputName);
                if (translatedShaderInput == null)
                {
                    translatedShaderInput = shader.addInput(inputName, translationGraphOutput.getType());
                }
                translatedShaderInput.setConnectedOutput(translatedOutput);
            }
        }

        // ShaderTranslator Protected Members
        protected NodeGraph _graph;

        protected Node _translationNode;
    }
}
