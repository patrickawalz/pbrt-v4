// MaterialX is Copyright Contributors to the MaterialX Project and is licensed
// under Apache-2.0. This file is a C# port of MaterialXGenOsl/OslNetworkShaderGenerator.h and OslNetworkShaderGenerator.cpp.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using static MaterialX.Mx;

namespace MaterialX
{
    // OslNetworkShaderGenerator Definition
    // OSL (Open Shading Language) Network shader generator.
    // Generates a command string that OSL can use to build a ShaderGroup.
    public class OslNetworkShaderGenerator : OslShaderGenerator
    {
        // OslNetworkShaderGenerator Public Methods
        // Constructor.
        public OslNetworkShaderGenerator(TypeSystem typeSystem) : base(typeSystem)
        {
            _syntax = OslNetworkSyntax.create(typeSystem);
        }

        // Creator function.
        // If a TypeSystem is not provided it will be created internally.
        // Optionally pass in an externally created TypeSystem here,
        // if you want to keep type descriptions alive after the lifetime
        // of the shader generator.
        public static ShaderGenerator create(TypeSystem typeSystem = null)
        {
            return new OslNetworkShaderGenerator(typeSystem != null ? typeSystem : TypeSystem.create());
        }

        public override ShaderNodeImpl createShaderNodeImplForImplementation(Implementation implElement)
        {
            return OsoNode.create();
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
            ShaderGraph graph = shader.getGraph();
            ShaderStage stage = shader.getStage(Stage.PIXEL);

            if (context.getOptions().oslConnectCiWrapper)
            {
                addSetCiTerminalNode(graph, element.getDocument(), context);
            }

            Document document = element.getDocument();

            string lastNodeName = EMPTY_STRING;
            ShaderOutput lastOutput = null;
            List<string> connections = new List<string>();

            StringSet osoPaths = new StringSet();

            // Walk the node graph, emitting shaders and param declarations.
            foreach (ShaderNode node in graph.getNodes())
            {
                string nodeName = node.getName();

                foreach (ShaderInput input in node.getInputs())
                {
                    string inputName = input.getName();
                    _syntax.makeValidName(ref inputName);

                    ShaderOutput connection = input.getConnection();
                    if (connection == null || connection.getNode() == graph)
                    {
                        if (!input.hasAuthoredValue())
                            continue;

                        if (input.getName() == "backsurfaceshader" || input.getName() == "displacementshader")
                            continue; // FIXME: these aren't getting pruned by hasAuthoredValue

                        string value = _syntax.getValue(input);
                        if (value == "null_closure()")
                            continue;

                        // TODO: Figure out how to avoid special-casing struct-types in the generator, perhaps in the syntax?
                        TypeDesc inputType = input.getType();
                        if (inputType == Type.VECTOR2)
                        {
                            List<string> parts = splitString(value, " ");
                            emitLine(paramString(_syntax.getTypeName(Type.FLOAT), inputName + ".x", parts[0]), stage, false);
                            emitLine(paramString(_syntax.getTypeName(Type.FLOAT), inputName + ".y", parts[1]), stage, false);
                        }
                        else if (inputType == Type.VECTOR4)
                        {
                            List<string> parts = splitString(value, " ");
                            emitLine(paramString(_syntax.getTypeName(Type.FLOAT), inputName + ".x", parts[0]), stage, false);
                            emitLine(paramString(_syntax.getTypeName(Type.FLOAT), inputName + ".y", parts[1]), stage, false);
                            emitLine(paramString(_syntax.getTypeName(Type.FLOAT), inputName + ".z", parts[2]), stage, false);
                            emitLine(paramString(_syntax.getTypeName(Type.FLOAT), inputName + ".w", parts[3]), stage, false);
                        }
                        else if (inputType == Type.COLOR4)
                        {
                            List<string> parts = splitString(value, " ");
                            emitLine(paramString(_syntax.getTypeName(Type.COLOR3), inputName + ".rgb", parts[0] + " " + parts[1] + " " + parts[2]), stage, false);
                            emitLine(paramString(_syntax.getTypeName(Type.FLOAT), inputName + ".a", parts[3]), stage, false);
                        }
                        else
                        {
                            emitLine(paramString(_syntax.getTypeName(input.getType()), inputName, value), stage, false);
                        }
                    }
                    else
                    {
                        string connName = connection.getName();
                        _syntax.makeValidName(ref connName);

                        string connect = connectString(connection.getNode().getName(), connName, nodeName, inputName);
                        // Save connect emits for the end, because they can't come
                        // before both connected shaders have been declared.
                        connections.Add(connect);
                    }
                }

                // Keep track of the root output, so we can connect it to our setCi node
                lastOutput = node.getOutput(0);

                ShaderNodeImpl impl = node.getImplementation();
                OsoNode osoNodeImpl = (OsoNode)(impl);

                string osoPath = osoNodeImpl.getOsoPath();
                osoPaths.Add(osoPath);

                emitLine("shader " + osoNodeImpl.getOsoName() + " " + nodeName + " ;", stage, false);
                lastNodeName = nodeName;
            }

            if (lastOutput == null)
            {
                Console.Write("Invalid shader\n");
                return null;
            }

            foreach (string connect in connections)
            {
                emitLine(connect, stage, false);
            }

            // From our set of required oso paths, build the path string that oslc will need.
            string osoPathStr = EMPTY_STRING;
            string separator = "";
            foreach (string osoPath in osoPaths)
            {
                FilePath fullOsoPath = context.resolveSourceFile(osoPath, "");
                string fullOsoPathStr = fullOsoPath.asString();

                osoPathStr += separator + fullOsoPathStr;
                separator = ",";
            }

            shader.setAttribute("osoPath", Value.createValue<string>(osoPathStr));

            return shader;
        }

        // OslNetworkShaderGenerator Public Members
        // Unique identifier for this generator target
        public const string TARGET = "genoslnetwork";

        // OslNetworkShaderGenerator Protected Methods
        // Create and initialize a new OSL shader for shader generation.
        protected override Shader createShader(string name, Element element, GenContext context)
        {
            // Create the root shader graph
            ShaderGraph graph = ShaderGraph.create(null, name, element, context);
            Shader shader = new Shader(name, graph);

            // Create our stage.
            ShaderStage stage = createStage(Stage.PIXEL, shader);
            stage.createUniformBlock(OSLNetwork.UNIFORMS);
            stage.createInputBlock(OSLNetwork.INPUTS);
            stage.createOutputBlock(OSLNetwork.OUTPUTS);

            // Create shader variables for all nodes that need this.
            createVariables(graph, context, shader);

            // Create uniforms for the published graph interface.
            VariableBlock uniforms = stage.getUniformBlock(OSLNetwork.UNIFORMS);
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
            VariableBlock outputs = stage.getOutputBlock(OSLNetwork.OUTPUTS);
            foreach (ShaderGraphOutputSocket outputSocket in graph.getOutputSockets())
            {
                outputs.add(outputSocket.getSelf());
            }

            return shader;
        }

        // OslNetworkShaderGenerator Private Methods
        private static string paramString(string paramType, string paramName, string paramValue)
        {
            return "param " + paramType + " " + paramName + " " + paramValue + " ;";
        }

        private static string connectString(string fromNode, string fromName, string toNode, string toName)
        {
            return "connect " + fromNode + "." + fromName + " " + toNode + "." + toName + " ;";
        }
    }

    // OSLNetwork Definition
    // Identifiers for OSL variable blocks (the namespace OSLNetwork in the C++).
    public static class OSLNetwork
    {
        public const string UNIFORMS = "u";
        public const string INPUTS = "i";
        public const string OUTPUTS = "o";
    }
}
