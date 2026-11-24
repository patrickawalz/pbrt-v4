// MaterialX is Copyright Contributors to the MaterialX Project and is licensed
// under Apache-2.0. This file is a C# port of MaterialXGenOsl/OslShaderGenerator.h and OslShaderGenerator.cpp.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using static MaterialX.Mx;

namespace MaterialX
{
    // OslShaderGenerator Definition
    // Base class for OSL (Open Shading Language) shader generators.
    // A generator for a specific OSL target should be derived from this class.
    public class OslShaderGenerator : ShaderGenerator
    {
        // OslShaderGenerator Public Methods
        // Constructor.
        public OslShaderGenerator(TypeSystem typeSystem) : base(typeSystem, OslSyntax.create(typeSystem))
        {
        }

        // Creator function.
        // If a TypeSystem is not provided it will be created internally.
        // Optionally pass in an externally created TypeSystem here,
        // if you want to keep type descriptions alive after the lifetime
        // of the shader generator.
        public static ShaderGenerator create(TypeSystem typeSystem = null)
        {
            return new OslShaderGenerator(typeSystem != null ? typeSystem : TypeSystem.create());
        }

        // Return a unique identifier for the target this generator is for
        public override string getTarget()
        {
            return TARGET;
        }

        // Generate a shader starting from the given element, translating
        // the element and all dependencies upstream into shader code.
        public override Shader generate(string name, Element element, GenContext context)
        {
            Shader shader = createShader(name, element, context);

            // Request fixed floating-point notation for consistency across targets.
            using ScopedFloatFormatting fmt = new ScopedFloatFormatting(Value.FloatFormat.FloatFormatFixed);

            ShaderGraph graph = shader.getGraph();

            if (context.getOptions().oslConnectCiWrapper)
            {
                addSetCiTerminalNode(graph, element.getDocument(), context);
            }

            ShaderStage stage = shader.getStage(Stage.PIXEL);

            emitLibraryIncludes(stage, context);

            // Add global constants and type definitions
            emitTypeDefinitions(context, stage);
            emitLine("#define M_FLOAT_EPS 1e-8", stage, false);
            emitLine("closure color null_closure() { closure color null_closure = 0; return null_closure; } ", stage, false);
            emitLineBreak(stage);

            // Set the include file to use for uv transformations,
            // depending on the vertical flip flag.
            if (context.getOptions().fileTextureVerticalFlip)
            {
                _tokenSubstitutions[ShaderGenerator.T_FILE_TRANSFORM_UV] = "mx_transform_uv_vflip.osl";
            }
            else
            {
                _tokenSubstitutions[ShaderGenerator.T_FILE_TRANSFORM_UV] = "mx_transform_uv.osl";
            }

            // Emit function definitions for all nodes
            emitFunctionDefinitions(graph, context, stage);

            // Emit shader type, determined from the first
            // output if there are multiple outputs.
            ShaderGraphOutputSocket outputSocket0 = graph.getOutputSocket(0);
            if (outputSocket0.getType() == Type.SURFACESHADER)
            {
                emitString("surface ", stage);
            }
            else if (outputSocket0.getType() == Type.VOLUMESHADER)
            {
                emitString("volume ", stage);
            }
            else
            {
                emitString("shader ", stage);
            }

            // Begin shader signature. Note that the function name is already sanitized.
            string functionName = shader.getName();
            setFunctionName(functionName, stage);
            emitLine(functionName, stage, false);

            List<ShaderMetadata> metadata = graph.getMetadata();
            bool haveShaderMetaData = metadata != null && metadata.Count != 0;

            // Always emit node information
            emitScopeBegin(stage, Syntax.Punctuation.DOUBLE_SQUARE_BRACKETS);
            emitLine("string mtlx_category = \"" + element.getCategory() + "\"" + Syntax.COMMA, stage, false);
            emitLine("string mtlx_name = \"" + element.getQualifiedName(element.getName()) + "\"" +
                         (haveShaderMetaData ? Syntax.COMMA : EMPTY_STRING),
                     stage, false);

            // Add any metadata if set on the graph.
            if (haveShaderMetaData)
            {
                for (int j = 0; j < metadata.Count; ++j)
                {
                    ShaderMetadata data = (metadata)[j];
                    string delim = (j == metadata.Count - 1) ? EMPTY_STRING : Syntax.COMMA;
                    string dataType = _syntax.getTypeName(data.type);
                    string dataValue = _syntax.getValue(data.type, data.value, true);
                    emitLine(dataType + " " + data.name + " = " + dataValue + delim, stage, false);
                }
            }
            emitScopeEnd(stage, false, false);
            emitLineEnd(stage, false);

            emitScopeBegin(stage, Syntax.Punctuation.PARENTHESES);

            // Emit shader inputs
            emitShaderInputs(stage.getInputBlock(OSL.INPUTS), stage);
            emitShaderInputs(stage.getUniformBlock(OSL.UNIFORMS), stage);

            // Emit shader outputs
            VariableBlock outputs = stage.getOutputBlock(OSL.OUTPUTS);
            emitShaderOutputs(outputs, stage);

            // End shader signature
            emitScopeEnd(stage);

            // Begin shader body
            emitFunctionBodyBegin(graph, context, stage);

            // Emit constants
            VariableBlock constants = stage.getConstantBlock();
            if (constants.size() != 0)
            {
                emitVariableDeclarations(constants, _syntax.getConstantQualifier(), Syntax.SEMICOLON, context, stage);
                emitLineBreak(stage);
            }

            // Inputs of type 'filename' has been generated into two shader inputs.
            // So here we construct a single 'textureresource' from these inputs,
            // to be used further downstream. See emitShaderInputs() for details.
            VariableBlock inputs = stage.getUniformBlock(OSL.UNIFORMS);
            for (int i = 0; i < inputs.size(); ++i)
            {
                ShaderPort input = inputs[i];
                if (input.getType() == Type.FILENAME)
                {
                    // Construct the textureresource variable.
                    string newVariableName = input.getVariable() + "_";
                    string type = _syntax.getTypeName(input.getType());
                    emitLine(type + newVariableName + " = {" + input.getVariable() + ", " + input.getVariable() + "_colorspace}", stage);

                    // Update the variable name to be used downstream.
                    input.setVariable(newVariableName);
                }
            }

            // Emit function calls for all nodes in the graph, starting each output
            // port and walking backwards along the incoming connections
            foreach (ShaderGraphOutputSocket socket in graph.getOutputSockets())
            {
                if (socket.getConnection() != null)
                {
                    ShaderNode upstream = socket.getConnection().getNode();
                    if (upstream.getParent() == graph)
                    {
                        emitAllDependentFunctionCalls(upstream, context, stage);
                    }
                }
            }

            // Assign results to final outputs.
            for (int i = 0; i < outputs.size(); ++i)
            {
                ShaderGraphOutputSocket outputSocket = graph.getOutputSocket(i);
                emitLine(outputSocket.getVariable() + " = " + getUpstreamResult(outputSocket, context), stage);
            }

            // End shader body
            emitFunctionBodyEnd(graph, context, stage);

            // Perform token substitution
            replaceTokens(_tokenSubstitutions, stage);

            return shader;
        }

        // Add all function calls for a node, and all upstream nodes.
        public void emitAllDependentFunctionCalls(ShaderNode node, GenContext context, ShaderStage stage)
        {
            // Check if it's emitted already.
            if (!stage.isEmitted(node, context))
            {
                // Emit function calls for upstream connected nodes
                foreach (ShaderInput input in node.getInputs())
                {
                    ShaderNode upstream = input.getConnectedSibling();
                    if (upstream != null)
                    {
                        emitAllDependentFunctionCalls(upstream, context, stage);
                    }
                }
                stage.addFunctionCall(node, context);
            }
        }

        // OslShaderGenerator Public Members
        // Unique identifier for this generator target
        public const string TARGET = "genosl";

        // OslShaderGenerator Public Methods
        // Register metadata that should be exported to the generated shaders.
        public override void registerShaderMetadata(Document doc, GenContext context)
        {
            // Register all standard metadata.
            base.registerShaderMetadata(doc, context);

            ShaderMetadataRegistry registry = context.getUserData<ShaderMetadataRegistry>(ShaderMetadataRegistry.USER_DATA_NAME);
            if (registry == null)
            {
                throw new ExceptionShaderGenError("Registration of metadata failed");
            }

            // Rename the standard metadata names to corresponding OSL metadata names.
            StringMap nameRemapping = new StringMap
            {
                { ValueElement.UI_NAME_ATTRIBUTE, "label" },
                { ValueElement.UI_FOLDER_ATTRIBUTE, "page" },
                { ValueElement.UI_MIN_ATTRIBUTE, "min" },
                { ValueElement.UI_MAX_ATTRIBUTE, "max" },
                { ValueElement.UI_SOFT_MIN_ATTRIBUTE, "slidermin" },
                { ValueElement.UI_SOFT_MAX_ATTRIBUTE, "slidermax" },
                { ValueElement.UI_STEP_ATTRIBUTE, "sensitivity" },
                { ValueElement.DOC_ATTRIBUTE, "help" }
            };
            foreach (KeyValuePair<string, string> it in nameRemapping)
            {
                ShaderMetadata data = registry.findMetadata(it.Key);
                if (data != null)
                {
                    data.name = it.Value;
                }
            }
        }

        // OslShaderGenerator Protected Methods
        // Create and initialize a new OSL shader for shader generation.
        protected virtual Shader createShader(string name, Element element, GenContext context)
        {
            // Create the root shader graph
            ShaderGraph graph = ShaderGraph.create(null, name, element, context);

            // Special handling for surfaceshader type output - if we have a material
            // that outputs a single surfaceshader then we will implicitly add a surfacematerial
            // node to create the final closure color - the surfaceshader type is a struct and needs
            // flattening to a single closure in the surfacematerial node.
            List<ShaderInput> outputSockets = graph.getOutputSockets();
            ShaderInput singleOutput = outputSockets.Count == 1 ? outputSockets[0] : null;

            bool isSurfaceShaderOutput = context.getOptions().oslImplicitSurfaceShaderConversion && singleOutput != null && singleOutput.getType() == Type.SURFACESHADER;

            if (isSurfaceShaderOutput)
            {
                graph.inlineNodeBeforeOutput(outputSockets[0], "_surfacematerial_", "ND_surfacematerial", "surfaceshader", "out", context);
            }

            Shader shader = new Shader(name, graph);

            // Create our stage.
            ShaderStage stage = createStage(Stage.PIXEL, shader);
            stage.createUniformBlock(OSL.UNIFORMS);
            stage.createInputBlock(OSL.INPUTS);
            stage.createOutputBlock(OSL.OUTPUTS);

            // Create shader variables for all nodes that need this.
            createVariables(graph, context, shader);

            // Create uniforms for the published graph interface.
            VariableBlock uniforms = stage.getUniformBlock(OSL.UNIFORMS);
            foreach (ShaderGraphInputSocket inputSocket in graph.getInputSockets())
            {
                // Only for inputs that are connected/used internally,
                // and are editable by users.
                if (inputSocket.getConnections().Count != 0 && graph.isEditable(inputSocket))
                {
                    uniforms.add(inputSocket.getSelf());
                }
            }

            // Create outputs from the graph interface.
            VariableBlock outputs = stage.getOutputBlock(OSL.OUTPUTS);
            foreach (ShaderGraphOutputSocket outputSocket in graph.getOutputSockets())
            {
                outputs.add(outputSocket.getSelf());
            }

            return shader;
        }

        // Emit include headers needed by the generated shader code.
        protected virtual void emitLibraryIncludes(ShaderStage stage, GenContext context)
        {
            string INCLUDE_PREFIX = "#include \"";
            string INCLUDE_SUFFIX = "\"";
            string[] INCLUDE_FILES = new string[]
            {
                "mx_funcs.h"
            };

            foreach (string file in INCLUDE_FILES)
            {
                FilePath path = context.resolveSourceFile(file, new FilePath());

                // Force path to use slash since backslash even if escaped
                // gives problems when saving the source code to file.
                string pathStr = path.asString();
                pathStr = pathStr.Replace('\\', '/');

                emitLine(INCLUDE_PREFIX + pathStr + INCLUDE_SUFFIX, stage, false);
            }

            emitLineBreak(stage);
        }

        // Emit a block of shader inputs.
        protected virtual void emitShaderInputs(VariableBlock inputs, ShaderStage stage)
        {
            Dictionary<string, string> GEOMPROP_DEFINITIONS = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "Pobject", "transform(\"object\", P)" },
                { "Pworld", "P" },
                { "Nobject", "transform(\"object\", N)" },
                { "Nworld", "N" },
                { "Tobject", "normalize(transform(\"object\", dPdu))" },
                { "Tworld", "normalize(dPdu)" },
                { "Bobject", "normalize(cross(transform(\"object\", N), transform(\"object\", dPdu)))" },
                { "Bworld", "normalize(cross(N, dPdu))" },
                { "UV0", "{u,v}" },
                { "Vworld", "I" }
            };

            for (int i = 0; i < inputs.size(); ++i)
            {
                ShaderPort input = inputs[i];
                string type = _syntax.getTypeName(input.getType());

                if (input.getType() == Type.FILENAME)
                {
                    // Shader inputs of type 'filename' (textures) need special handling.
                    // In OSL codegen a 'filename' is translated to the custom type 'textureresource',
                    // which is a struct containing a file string and a colorspace string.
                    // For the published shader interface we here split this into two separate inputs,
                    // which gives a nicer shader interface with widget metadata on each input.

                    Value value = input.getValue();
                    string valueStr = value != null ? value.getValueString() : EMPTY_STRING;

                    // Add the file string input
                    emitLineBegin(stage);
                    emitString("string " + input.getVariable() + " = \"" + valueStr + "\"", stage);
                    emitMetadata(input, stage);
                    emitString(",", stage);
                    emitLineEnd(stage, false);

                    // Add the colorspace string input
                    emitLineBegin(stage);
                    emitString("string " + input.getVariable() + "_colorspace = \"" + input.getColorSpace() + "\"", stage);
                    emitLineEnd(stage, false);
                    emitScopeBegin(stage, Syntax.Punctuation.DOUBLE_SQUARE_BRACKETS);
                    emitLine("string widget = \"colorspace\"", stage, false);
                    emitScopeEnd(stage, false, false);
                }
                else
                {
                    emitLineBegin(stage);
                    emitString(type + " " + input.getVariable(), stage);

                    string value = _syntax.getValue(input, true);
                    string geomprop = input.getGeomProp();
                    if (geomprop.Length != 0)
                    {
                        string found;
                        if (GEOMPROP_DEFINITIONS.TryGetValue(geomprop, out found))
                        {
                            value = found;
                        }
                    }
                    if (value.Length == 0)
                    {
                        value = _syntax.getDefaultValue(input.getType());
                    }

                    emitString(" = " + value, stage);
                    emitMetadata(input, stage);
                }

                if (i < inputs.size())
                {
                    emitString(",", stage);
                }

                emitLineEnd(stage, false);
            }
        }

        // Emit a block of shader outputs.
        protected virtual void emitShaderOutputs(VariableBlock outputs, ShaderStage stage)
        {
            for (int i = 0; i < outputs.size(); ++i)
            {
                ShaderPort output = outputs[i];
                TypeDesc outputType = output.getType();
                string type = _syntax.getOutputTypeName(outputType);
                string value = _syntax.getDefaultValue(outputType, true);
                string delim = (i == outputs.size() - 1) ? EMPTY_STRING : Syntax.COMMA;
                emitLine(type + " " + output.getVariable() + " = " + value + delim, stage, false);
            }
        }

        // Emit metadata for a shader parameter.
        protected virtual void emitMetadata(ShaderPort port, ShaderStage stage)
        {
            Dictionary<TypeDesc, ShaderMetadata> UI_WIDGET_METADATA = new Dictionary<TypeDesc, ShaderMetadata>
            {
                { Type.FLOAT, new ShaderMetadata("widget", Type.STRING, Type.STRING.createValueFromStrings("number")) },
                { Type.INTEGER, new ShaderMetadata("widget", Type.STRING, Type.STRING.createValueFromStrings("number")) },
                { Type.FILENAME, new ShaderMetadata("widget", Type.STRING, Type.STRING.createValueFromStrings("filename")) },
                { Type.BOOLEAN, new ShaderMetadata("widget", Type.STRING, Type.STRING.createValueFromStrings("checkBox")) }
            };

            HashSet<TypeDesc> METADATA_TYPE_BLACKLIST = new HashSet<TypeDesc>
            {
                Type.VECTOR2,  // Custom struct types doesn't support metadata declarations.
                Type.VECTOR4,  //
                Type.COLOR4,   //
                Type.FILENAME, //
                Type.BSDF      //
            };

            ShaderMetadata widgetMetadata;
            UI_WIDGET_METADATA.TryGetValue(port.getType(), out widgetMetadata);
            List<ShaderMetadata> metadata = port.getMetadata();
            string geomprop = port.getGeomProp();

            if (widgetMetadata != null || (metadata != null && metadata.Count != 0) || geomprop.Length != 0)
            {
                List<string> metadataLines = new List<string>();
                if (metadata != null)
                {
                    for (int j = 0; j < metadata.Count; ++j)
                    {
                        ShaderMetadata data = (metadata)[j];
                        if (!METADATA_TYPE_BLACKLIST.Contains(data.type))
                        {
                            string delim = (widgetMetadata != null || j < metadata.Count - 1) ? Syntax.COMMA : EMPTY_STRING;
                            string dataType = _syntax.getTypeName(data.type);
                            string dataValue = _syntax.getValue(data.type, data.value, true);
                            metadataLines.Add(dataType + " " + data.name + " = " + dataValue + delim);
                        }
                    }
                }
                if (widgetMetadata != null)
                {
                    string delim = geomprop.Length == 0 ? EMPTY_STRING : Syntax.COMMA;
                    string dataType = _syntax.getTypeName(widgetMetadata.type);
                    string dataValue = _syntax.getValue(widgetMetadata.type, widgetMetadata.value, true);
                    metadataLines.Add(dataType + " " + widgetMetadata.name + " = " + dataValue + delim);
                }
                if (geomprop.Length != 0)
                {
                    string dataType = _syntax.getTypeName(Type.STRING);
                    metadataLines.Add(dataType + " mtlx_defaultgeomprop = \"" + geomprop + "\"");
                }
                if (metadataLines.Count != 0)
                {
                    emitLineEnd(stage, false);
                    emitScopeBegin(stage, Syntax.Punctuation.DOUBLE_SQUARE_BRACKETS);
                    foreach (string line in metadataLines)
                    {
                        emitLine(line, stage, false);
                    }
                    emitScopeEnd(stage, false, false);
                }
            }
        }

        protected void addSetCiTerminalNode(ShaderGraph graph, Document document, GenContext context)
        {
            string setCiNodeDefName = "ND_osl_set_ci";
            NodeDef setCiNodeDef = document.getNodeDef(setCiNodeDefName);

            Dictionary<TypeDesc, Value> outputModeMap = new Dictionary<TypeDesc, Value>();
            int index = 0;
            foreach (Input input in setCiNodeDef.getInputs())
            {
                string inputName = input.getName();
                if (stringStartsWith(inputName, "input_"))
                {
                    TypeDesc inputType = _typeSystem.getType(input.getType());
                    outputModeMap[inputType] = new TypedValue<int>(index++);
                }
            }

            foreach (ShaderInput output in graph.getOutputSockets())
            {
                TypeDesc outputType = output.getType();
                string typeName = outputType.getName();
                ShaderNode setCiNode = graph.inlineNodeBeforeOutput(output, "oslSetCi", setCiNodeDefName, "input_" + typeName, "out_ci", context);
                ShaderInput typeInput = setCiNode.getInput("output_mode");

                Value outputModeValue = outputModeMap[outputType];

                typeInput.setValue(outputModeValue);
            }
        }
    }

    // OSL Definition
    // Identifiers for OSL variable blocks (the namespace OSL in the C++).
    public static class OSL
    {
        public const string UNIFORMS = "u";
        public const string INPUTS = "i";
        public const string OUTPUTS = "o";
    }
}
