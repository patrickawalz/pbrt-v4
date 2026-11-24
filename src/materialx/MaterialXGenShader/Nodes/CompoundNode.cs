// MaterialX is Copyright Contributors to the MaterialX Project and is licensed
// under Apache-2.0. This file is a C# port of MaterialXGenShader/CompoundNode.h and CompoundNode.cpp.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using static MaterialX.Mx;

namespace MaterialX
{
    // CompoundNode Definition
    // Compound node implementation
    public class CompoundNode : ShaderNodeImpl
    {
        // CompoundNode Public Methods
        public static ShaderNodeImpl create()
        {
            return new CompoundNode();
        }

        public override void initialize(InterfaceElement element, GenContext context)
        {

            base.initialize(element, context);

            if (!element.isA<NodeGraph>())
            {
                throw new ExceptionShaderGenError("Element '" + element.getName() + "' is not a node graph implementation");
            }

            NodeGraph graph = (NodeGraph)(element);

            _functionName = graph.getName();
            context.getShaderGenerator().getSyntax().makeValidName(ref _functionName);

            // For compounds we do not want to publish all internal inputs
            // so always use the reduced interface for this graph.
            ShaderInterfaceType oldShaderInterfaceType = context.getOptions().shaderInterfaceType;
            context.getOptions().shaderInterfaceType = ShaderInterfaceType.SHADER_INTERFACE_REDUCED;
            _rootGraph = ShaderGraph.create(null, graph, context);
            context.getOptions().shaderInterfaceType = oldShaderInterfaceType;

            // Set hash using the function name.
            // TODO: Could be improved to include the full function signature.
            _hash = stringHash(_functionName);
        }

        public override void addClassification(ShaderNode node)
        {
            // Add classification from the graph implementation.
            node.addClassification(_rootGraph.getClassification());
        }

        public override void createVariables(ShaderNode node, GenContext context, Shader shader)
        {
            // Gather shader inputs from all child nodes
            foreach (ShaderNode childNode in _rootGraph.getNodes())
            {
                childNode.getImplementation().createVariables(childNode, context, shader);
            }
        }

        public override void emitFunctionDefinition(ShaderNode node, GenContext context, ShaderStage stage)
        {

            if (stage.getName() == Stage.PIXEL)
            {
                ShaderGenerator shadergen = context.getShaderGenerator();

                // Emit functions for all child nodes
                shadergen.emitFunctionDefinitions(_rootGraph, context, stage);

                // Begin function signature.
                shadergen.emitLineBegin(stage);
                shadergen.emitString("void " + _functionName + "(", stage);

                shadergen.emitClosureDataParameter(node, context, stage);
                // if (context.getShaderGenerator().nodeNeedsClosureData(node))
                // {
                //     shadergen.emitString(HW::CLOSURE_DATA_TYPE + " " + HW::CLOSURE_DATA_ARG + ", ", stage);
                // }

                string delim = EMPTY_STRING;

                // Add all inputs
                foreach (ShaderGraphInputSocket inputSocket in _rootGraph.getInputSockets())
                {
                    shadergen.emitString(delim, stage);
                    shadergen.emitFunctionDefinitionParameter(inputSocket, false, context, stage);
                    delim = ", ";
                }

                // Add all outputs
                foreach (ShaderGraphOutputSocket outputSocket in _rootGraph.getOutputSockets())
                {
                    shadergen.emitString(delim, stage);
                    shadergen.emitFunctionDefinitionParameter(outputSocket, true, context, stage);
                    delim = ", ";
                }

                // End function signature.
                shadergen.emitString(")", stage);
                shadergen.emitLineEnd(stage, false);

                // Begin function body.
                shadergen.emitFunctionBodyBegin(_rootGraph, context, stage);

                if (nodeOutputIsClosure(node))
                {
                    // Emit all texturing nodes. These are inputs to the
                    // closure nodes and need to be emitted first.
                    shadergen.emitFunctionCalls(_rootGraph, context, stage, ShaderNode.Classification.TEXTURE);

                    // Emit function calls for internal closures nodes connected to the graph sockets.
                    // These will in turn emit function calls for any dependent closure nodes upstream.
                    foreach (ShaderGraphOutputSocket outputSocket in _rootGraph.getOutputSockets())
                    {
                        if (outputSocket.getConnection() != null)
                        {
                            ShaderNode upstream = outputSocket.getConnection().getNode();
                            // Its important that the classification check here matches the logic inside
                            // nodeOutputIsClosure() used above.
                            if (upstream.getParent() == _rootGraph &&
                                (upstream.hasClassification(ShaderNode.Classification.CLOSURE) ||
                                    upstream.hasClassification(ShaderNode.Classification.SHADER) ||
                                    upstream.hasClassification(ShaderNode.Classification.MATERIAL)))
                            {
                                shadergen.emitFunctionCall(upstream, context, stage);
                            }
                        }
                    }
                }
                else
                {
                    shadergen.emitFunctionCalls(_rootGraph, context, stage);
                }

                // Emit final results
                foreach (ShaderGraphOutputSocket outputSocket in _rootGraph.getOutputSockets())
                {
                    string result = shadergen.getUpstreamResult(outputSocket, context);
                    shadergen.emitLine(outputSocket.getVariable() + " = " + result, stage);
                }

                // End function body.
                shadergen.emitFunctionBodyEnd(_rootGraph, context, stage);
            }
        }

        public override void emitFunctionCall(ShaderNode node, GenContext context, ShaderStage stage)
        {

            ShaderGenerator shadergen = context.getShaderGenerator();

            if (stage.getName() == Stage.VERTEX)
            {
                // Emit function calls for all child nodes to the vertex shader stage
                shadergen.emitFunctionCalls(_rootGraph, context, stage);
            }

            if (stage.getName() == Stage.PIXEL)
            {
                if (nodeOutputIsClosure(node))
                {
                    // Emit calls for any closure dependencies upstream from this nodedef
                    shadergen.emitDependentFunctionCalls(node, context, stage, ShaderNode.Classification.CLOSURE);
                }

                // Declare the output variables.
                emitOutputVariables(node, context, stage);

                // Begin function call.
                shadergen.emitLineBegin(stage);
                shadergen.emitString(_functionName + "(", stage);

                // Add an argument for closure data if needed
                shadergen.emitClosureDataArg(node, context, stage);
                // if (context.getShaderGenerator().nodeNeedsClosureData(node))
                // {
                //     shadergen.emitString(HW::CLOSURE_DATA_ARG + ", ", stage);
                // }

                string delim = EMPTY_STRING;

                // Emit inputs.
                foreach (ShaderInput input in node.getInputs())
                {
                    shadergen.emitString(delim, stage);
                    shadergen.emitInput(input, context, stage);
                    delim = ", ";
                }

                // Emit outputs.
                for (int i = 0; i < node.numOutputs(); ++i)
                {
                    shadergen.emitString(delim, stage);
                    shadergen.emitOutput(node.getOutput(i), false, false, context, stage);
                    delim = ", ";
                }

                // End function call
                shadergen.emitString(")", stage);
                shadergen.emitLineEnd(stage);
            }
        }

        public override ShaderGraph getGraph()
        {
            return _rootGraph;
        }

        // CompoundNode Protected Members
        protected ShaderGraph _rootGraph;

        protected string _functionName = "";
    }
}
