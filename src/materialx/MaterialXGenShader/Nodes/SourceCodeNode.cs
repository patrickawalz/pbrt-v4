// MaterialX is Copyright Contributors to the MaterialX Project and is licensed
// under Apache-2.0. This file is a C# port of MaterialXGenShader/SourceCodeNode.h and SourceCodeNode.cpp.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using static MaterialX.Mx;

namespace MaterialX
{
    // SourceCodeNode Definition
    // Implementation for a node using data-driven static source code.
    // This is the default implementation used for all nodes that
    // do not have a custom ShaderNodeImpl class.
    public class SourceCodeNode : ShaderNodeImpl
    {
        // SourceCodeNode Private Members
        private const string INLINE_VARIABLE_PREFIX = "{{";
        private const string INLINE_VARIABLE_SUFFIX = "}}";

        // SourceCodeNode Public Methods
        public static ShaderNodeImpl create()
        {
            return new SourceCodeNode();
        }

        public override void initialize(InterfaceElement element, GenContext context)
        {
            base.initialize(element, context);

            if (!element.isA<Implementation>())
            {
                throw new ExceptionShaderGenError("Element '" + element.getName() + "' is not an Implementation element");
            }

            Implementation impl = (Implementation)(element);

            // Get source code from either an attribute or a file.
            _functionSource = impl.getAttribute("sourcecode");
            if (_functionSource.Length == 0)
            {
                resolveSourceCode(element, context);
            }

            // Find the function name to use
            // If no function is given the source will be inlined.
            _functionName = impl.getAttribute("function");

            _inlined = _functionName.Length == 0;
            if (!_inlined)
            {
                // Make sure the function name is valid.
                string validFunctionName = _functionName;
                context.getShaderGenerator().getSyntax().makeValidName(ref validFunctionName);
                if (_functionName != validFunctionName)
                {
                    throw new ExceptionShaderGenError("Function name '" + _functionName +
                                                  "' used by implementation '" + impl.getName() + "' is not a valid identifier.");
                }
            }
            else
            {
                StringMap newlines = new StringMap();
                newlines["\n"] = "";
                _functionSource = replaceSubstrings(_functionSource, newlines);
            }

            // Set hash using the function name.
            // TODO: Could be improved to include the full function signature.
            _hash = stringHash(_functionName);
        }

        public override void emitFunctionDefinition(ShaderNode node, GenContext context, ShaderStage stage)
        {
            if (_inlined || _functionSource.Length == 0 || _sourceFilename.isEmpty())
            {
                return;
            }

            if (stage.getName() == Stage.PIXEL)
            {
                if (!stage.hasSourceDependency(_sourceFilename))
                {
                    ShaderGenerator shadergen = context.getShaderGenerator();
                    shadergen.emitBlock(_functionSource, _sourceFilename, context, stage);
                    shadergen.emitLineBreak(stage);
                    stage.addSourceDependency(_sourceFilename);
                }
            }
        }

        public override void emitFunctionCall(ShaderNode node, GenContext context, ShaderStage stage)
        {
            if (stage.getName() == Stage.PIXEL)
            {
                ShaderGenerator shadergen = context.getShaderGenerator();

                if (nodeOutputIsClosure(node))
                {
                    // Emit calls for any closure dependencies upstream from this nodedef
                    shadergen.emitDependentFunctionCalls(node, context, stage, ShaderNode.Classification.CLOSURE);
                }

                if (_inlined)
                {
                    // An inline function call

                    int pos = 0;
                    int i = _functionSource.IndexOf(INLINE_VARIABLE_PREFIX, StringComparison.Ordinal);
                    StringSet variableNames = new StringSet();
                    List<string> code = new List<string>();
                    while (i != -1)
                    {
                        code.Add(_functionSource.Substring(pos, i - pos));
                        int j = _functionSource.IndexOf(INLINE_VARIABLE_SUFFIX, i + 2, StringComparison.Ordinal);
                        if (j == -1)
                        {
                            throw new ExceptionShaderGenError("Malformed inline expression in implementation for node " + node.getName());
                        }

                        string variable = _functionSource.Substring(i + 2, j - i - 2);
                        ShaderInput input = node.getInput(variable);
                        if (input == null)
                        {
                            throw new ExceptionShaderGenError("Could not find an input named '" + variable +
                                                          "' on node '" + node.getName() + "'");
                        }

                        if (input.getConnection() != null)
                        {
                            code.Add(shadergen.getUpstreamResult(input, context));
                        }
                        else
                        {
                            string variableName = input.getVariable() + "_tmp";
                            if (!variableNames.Contains(variableName))
                            {
                                ShaderPort v = new ShaderPort(null, input.getType(), variableName, input.getValue());
                                shadergen.emitLineBegin(stage);
                                Syntax syntax = shadergen.getSyntax();
                                string valueStr = (v.getValue() != null ? syntax.getValue(v.getType(), v.getValue()) : syntax.getDefaultValue(v.getType()));
                                string qualifier = syntax.getConstantQualifier();
                                string str = qualifier.Length == 0 ? EMPTY_STRING : qualifier + " ";
                                str += syntax.getTypeName(v.getType()) + " " + v.getVariable();
                                str += valueStr.Length == 0 ? EMPTY_STRING : " = " + valueStr;
                                shadergen.emitString(str, stage);
                                shadergen.emitLineEnd(stage);
                                variableNames.Add(variableName);
                            }
                            code.Add(variableName);
                        }

                        pos = j + 2;
                        i = _functionSource.IndexOf(INLINE_VARIABLE_PREFIX, pos, StringComparison.Ordinal);
                    }
                    code.Add(_functionSource.Substring(pos));

                    shadergen.emitLineBegin(stage);
                    shadergen.emitOutput(node.getOutput(), true, false, context, stage);
                    shadergen.emitString(" = ", stage);
                    foreach (string c in code)
                    {
                        shadergen.emitString(c, stage);
                    }
                    shadergen.emitLineEnd(stage);
                }
                else
                {
                    // An ordinary source code function call

                    // Declare the output variables.
                    emitOutputVariables(node, context, stage);

                    shadergen.emitLineBegin(stage);

                    // Emit function name.
                    shadergen.emitString(_functionName + "(", stage);

                    shadergen.emitClosureDataArg(node, context, stage);
                    // if (context.getShaderGenerator().nodeNeedsClosureData(node))
                    // {
                    //     shadergen.emitString(HW::CLOSURE_DATA_ARG + ", ", stage);
                    // }

                    string delim = EMPTY_STRING;

                    // Emit all inputs.
                    foreach (ShaderInput input in node.getInputs())
                    {
                        shadergen.emitString(delim, stage);
                        shadergen.emitInput(input, context, stage);
                        delim = ", ";
                    }

                    // Emit node outputs.
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
        }

        // SourceCodeNode Protected Methods
        // Resolve the source file and read the source code during the initialization of the node.
        protected virtual void resolveSourceCode(InterfaceElement element, GenContext context)
        {
            Implementation impl = (Implementation)(element);

            FilePath localPath = new FilePath(impl.getActiveSourceUri()).getParentPath();
            _sourceFilename = context.resolveSourceFile(impl.getAttribute("file"), localPath);
            _functionSource = readFile(_sourceFilename);
            if (_functionSource.Length == 0)
            {
                throw new ExceptionShaderGenError("Failed to get source code from file '" + _sourceFilename.asString() +
                                              "' used by implementation '" + impl.getName() + "'");
            }
        }

        // SourceCodeNode Protected Members
        protected bool _inlined = false;

        protected string _functionName = "";

        protected string _functionSource = "";

        protected FilePath _sourceFilename = new FilePath();
    }
}
