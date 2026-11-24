// MaterialX is Copyright Contributors to the MaterialX Project and is licensed
// under Apache-2.0. This file is a C# port of MaterialXGenShader/MaterialNode.h and MaterialNode.cpp.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using static MaterialX.Mx;

namespace MaterialX
{
    // MaterialNode Definition
    // Material node implementation
    public class MaterialNode : ShaderNodeImpl
    {
        // MaterialNode Public Methods
        public static ShaderNodeImpl create()
        {
            return new MaterialNode();
        }

        public override void addClassification(ShaderNode node)
        {
            ShaderInput surfaceshaderInput = node.getInput(ShaderNode.SURFACESHADER);
            if (surfaceshaderInput != null && surfaceshaderInput.getConnection() != null)
            {
                // This is a material node with a surfaceshader connected.
                // Add the classification from this shader.
                ShaderNode surfaceshaderNode = surfaceshaderInput.getConnection().getNode();
                if (!surfaceshaderNode.isAGraph())
                    node.addClassification(surfaceshaderNode.getClassification());
            }
        }

        public override void emitFunctionCall(ShaderNode _node, GenContext context, ShaderStage stage)
        {
            if (stage.getName() == Stage.PIXEL)
            {
                ShaderNode node = _node;
                ShaderInput surfaceshaderInput = node.getInput(ShaderNode.SURFACESHADER);

                if (surfaceshaderInput.getConnection() == null)
                {
                    // Just declare the output variable with default value.
                    emitOutputVariables(node, context, stage);
                    return;
                }

                ShaderGenerator shadergen = context.getShaderGenerator();
                Syntax syntax = shadergen.getSyntax();

                // Emit the function call for upstream surface shader.
                ShaderNode surfaceshaderNode = surfaceshaderInput.getConnection().getNode();
                if (surfaceshaderNode.isAGraph())
                {
                    emitOutputVariables(node, context, stage);
                    return;
                }
                shadergen.emitFunctionCall(surfaceshaderNode, context, stage);

                // Assign this result to the material output variable.
                ShaderOutput output = node.getOutput();
                shadergen.emitLine(syntax.getTypeName(output.getType()) + " " + output.getVariable() + " = " + surfaceshaderInput.getConnection().getVariable(), stage);
            }
        }
    }
}
