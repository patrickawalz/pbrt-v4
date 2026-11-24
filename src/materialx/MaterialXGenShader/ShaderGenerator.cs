// MaterialX is Copyright Contributors to the MaterialX Project and is licensed
// under Apache-2.0. This file is a C# port of MaterialXGenShader/ShaderGenerator.h and ShaderGenerator.cpp.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using static MaterialX.Mx;

namespace MaterialX
{
    // ShaderGenerator Definition
    // Base class for shader generators
    // All third-party shader generators should derive from this class.
    // Derived classes should use DECLARE_SHADER_GENERATOR / DEFINE_SHADER_GENERATOR
    // in their declaration / definition, and register with the Registry class.
    public partial class ShaderGenerator
    {
        // ShaderGenerator Public Methods
        // Return the name of the target this generator is for.
        public virtual string getTarget()
        {
            return EMPTY_STRING;
        }

        // Generate a shader starting from the given element, translating
        // the element and all dependencies upstream into shader code.
        public virtual Shader generate(string arg0, Element arg1, GenContext arg2)
        {
            return null;
        }

        // Start a new scope using the given bracket type.
        public virtual void emitScopeBegin(ShaderStage stage, Syntax.Punctuation punc = Syntax.Punctuation.CURLY_BRACKETS)
        {
            stage.beginScope(punc);
        }

        // End the current scope.
        public virtual void emitScopeEnd(ShaderStage stage, bool semicolon = false, bool newline = true)
        {
            stage.endScope(semicolon, newline);
        }

        // Start a new line.
        public virtual void emitLineBegin(ShaderStage stage)
        {
            stage.beginLine();
        }

        // End the current line.
        public virtual void emitLineEnd(ShaderStage stage, bool semicolon = true)
        {
            stage.endLine(semicolon);
        }

        // Add a line break.
        public virtual void emitLineBreak(ShaderStage stage)
        {
            stage.newLine();
        }

        // Add a string.
        public virtual void emitString(string str, ShaderStage stage)
        {
            stage.addString(str);
        }

        // Add a single line of code, optionally appending a semicolon.
        public virtual void emitLine(string str, ShaderStage stage, bool semicolon = true)
        {
            stage.addLine(str, semicolon);
        }

        // Add a single line code comment.
        public virtual void emitComment(string str, ShaderStage stage)
        {
            stage.addComment(str);
        }

        // Add a block of code.
        public virtual void emitBlock(string str, FilePath sourceFilename, GenContext context, ShaderStage stage)
        {
            stage.addBlock(str, sourceFilename, context);
        }

        // Add the contents of a standard library include file if not already present.
        // The library file prefix of the given context, if any, will be prepended
        // to the given filename.
        public virtual void emitLibraryInclude(FilePath filename, GenContext context, ShaderStage stage)
        {
            FilePath libraryPrefix = context.getOptions().libraryPrefix;
            FilePath fullFilename = libraryPrefix.isEmpty() ? filename : libraryPrefix / filename;
            FilePath resolvedFilename = context.resolveSourceFile(fullFilename, new FilePath());
            stage.addInclude(fullFilename, resolvedFilename, context);
        }

        // Add a value.
        public void emitValue(string value, ShaderStage stage)
        {
            stage.addValue(value);
        }

        public void emitValue(int value, ShaderStage stage)
        {
            stage.addValue(value);
        }

        public void emitValue(float value, ShaderStage stage)
        {
            stage.addValue(value);
        }

        // Add the function definition for a single node.
        public virtual void emitFunctionDefinition(ShaderNode node, GenContext context, ShaderStage stage)
        {
            stage.addFunctionDefinition(node, context);
        }

        // Emit the connected variable name for an input and output in a function definition (Used by CompoundNode)
        public virtual void emitFunctionDefinitionParameter(ShaderPort shaderPort, bool isOutput, GenContext context, ShaderStage stage)
        {
            if (isOutput)
            {
                emitString(_syntax.getOutputTypeName(shaderPort.getType()) + " " + shaderPort.getVariable(), stage);
            }
            else 
            {
                emitString(_syntax.getTypeName(shaderPort.getType()) + " " + shaderPort.getVariable(), stage);
            }
        }

        // Add all function definitions for a graph.
        public virtual void emitFunctionDefinitions(ShaderGraph graph, GenContext context, ShaderStage stage)
        {

            // Emit function definitions for all nodes in the graph.
            foreach (ShaderNode node in graph.getNodes())
            {
                emitFunctionDefinition(node, context, stage);
            }
        }

        // Add the function call for a single node.
        public virtual void emitFunctionCall(ShaderNode node, GenContext context, ShaderStage stage)
        {
            // Check if it's emitted already.
            if (!stage.isEmitted(node, context))
            {
                stage.addFunctionCall(node, context);
            }
        }

        // Add all function calls for a graph. If a classification mask is given only functions for
        // nodes matching this classification will be emitted.
        public virtual void emitFunctionCalls(ShaderGraph graph, GenContext context, ShaderStage stage, uint classification = 0u)
        {

            foreach (ShaderNode node in graph.getNodes())
            {
                if (classification == 0 || node.hasClassification(classification))
                {
                    emitFunctionCall(node, context, stage);
                }
            }
        }

        // Add function calls for nodes connected directly upstream from the given node.
        // If a classification mask is given only functions for nodes matching this classification
        // will be emitted.
        public virtual void emitDependentFunctionCalls(ShaderNode node, GenContext context, ShaderStage stage, uint classification = 0u)
        {
            foreach (ShaderInput input in node.getInputs())
            {
                ShaderNode upstream = input.getConnectedSibling();
                if (upstream != null && (classification == 0 || upstream.hasClassification(classification)))
                {
                    emitFunctionCall(upstream, context, stage);
                }
            }
        }

        // Emit code for starting a new function body.
        public virtual void emitFunctionBodyBegin(ShaderNode node, GenContext context, ShaderStage stage, Syntax.Punctuation punc = Syntax.Punctuation.CURLY_BRACKETS)
        {
            emitScopeBegin(stage, punc);
        }

        // Emit code for ending a function body.
        public virtual void emitFunctionBodyEnd(ShaderNode node, GenContext context, ShaderStage stage)
        {
            emitScopeEnd(stage);
            emitLineBreak(stage);
        }

        // Emit type definitions for all data types that needs it.
        public virtual void emitTypeDefinitions(GenContext context, ShaderStage stage)
        {
            // Emit typedef statements for all data types that have an alias
            foreach (TypeSyntax syntax in _syntax.getTypeSyntaxes())
            {
                if (syntax.getTypeDefinition().Length != 0)
                {
                    stage.addLine(syntax.getTypeDefinition(), false);
                }
            }
            stage.newLine();
        }

        // Emit the connected variable name for an input,
        // or constant value if the port is not connected
        public virtual void emitInput(ShaderInput input, GenContext context, ShaderStage stage)
        {
            stage.addString(getUpstreamResult(input, context));
        }

        // Emit the output variable name for an output, optionally including it's type
        // and default value assignment.
        public virtual void emitOutput(ShaderOutput output, bool includeType, bool assignValue, GenContext context, ShaderStage stage)
        {
            stage.addString(includeType ? _syntax.getTypeName(output.getType()) + " " + output.getVariable() : output.getVariable());

            // Look for any additional suffix to append
            string suffix = EMPTY_STRING;
            context.getOutputSuffix(output, ref suffix);
            if (suffix.Length != 0)
            {
                stage.addString(suffix);
            }

            if (assignValue)
            {
                string value = _syntax.getDefaultValue(output.getType());
                if (value.Length != 0)
                {
                    stage.addString(" = " + value);
                }
            }
        }

        // Emit definitions for all shader variables in a block.
        // @param block Block to emit.
        // @param qualifier Optional qualifier to add before the variable declaration.
        // @param separator Separator to use between the declarations.
        // @param context Context for generation.
        // @param stage The stage to emit code into.
        // @param assignValue If true the variables are initialized with their value.
        public virtual void emitVariableDeclarations(VariableBlock block, string qualifier, string separator, GenContext context, ShaderStage stage, bool assignValue = true)
        {
            for (int i = 0; i < block.size(); ++i)
            {
                emitLineBegin(stage);
                emitVariableDeclaration(block[i], qualifier, context, stage, assignValue);
                emitString(separator, stage);
                emitLineEnd(stage, false);
            }
        }

        // Emit definition of a single shader variable.
        // @param variable Shader port representing the variable.
        // @param qualifier Optional qualifier to add before the variable declaration.
        // @param context Context for generation.
        // @param stage The stage to emit code into.
        // @param assignValue If true the variable is initialized with its value.
        public virtual void emitVariableDeclaration(ShaderPort variable, string qualifier, GenContext context, ShaderStage stage, bool assignValue = true)
        {
            string str = qualifier.Length == 0 ? EMPTY_STRING : qualifier + " ";
            str += _syntax.getTypeName(variable.getType());

            bool haveArray = variable.getType().isArray() && variable.getValue() != null;
            if (haveArray)
            {
                str += _syntax.getArrayTypeSuffix(variable.getType(), variable.getValue());
            }

            str += " " + variable.getVariable();

            // If an array we need an array qualifier (suffix) for the variable name
            if (haveArray)
            {
                str += _syntax.getArrayVariableSuffix(variable.getType(), variable.getValue());
            }

            if (assignValue)
            {
                string valueStr = (variable.getValue() != null ?
                                         _syntax.getValue(variable.getType(), variable.getValue(), true) :
                                         _syntax.getDefaultValue(variable.getType(), true));
                str += valueStr.Length == 0 ? EMPTY_STRING : " = " + valueStr;
            }

            stage.addString(str);
        }

        // Return true if the node needs the additional ClosureData added
        public virtual bool nodeNeedsClosureData(ShaderNode node)
        {
            return false;
        }

        // Emit the closure data argument if required
        // Note this is an affordance for HwShaderGenerator
        public virtual void emitClosureDataArg(ShaderNode node, GenContext context, ShaderStage stage)
        {
        }

        // Emit the closure data parameter if required.
        // Note this is an affordance for HwShaderGenerator
        public virtual void emitClosureDataParameter(ShaderNode node, GenContext context, ShaderStage stage)
        {
        }

        // Return the result of an upstream connection or value for an input.
        public virtual string getUpstreamResult(ShaderInput input, GenContext context)
        {
            if (input.getConnection() == null)
            {
                return input.getValue() != null ? _syntax.getValue(input.getType(), input.getValue()) : _syntax.getDefaultValue(input.getType());
            }

            string variable = input.getConnection().getVariable();

            // Look for any additional suffix to append
            string suffix = EMPTY_STRING;
            context.getInputSuffix(input, ref suffix);
            if (suffix.Length != 0)
            {
                variable += suffix;
            }

            return variable;
        }

        // Return the syntax object for the language used by the code generator
        public Syntax getSyntax()
        {
            return _syntax;
        }

        // Register a shader node implementation for a given implementation element name
        public void registerImplementation(string name, Func<ShaderNodeImpl> creator)
        {
            _implFactory.registerClass(name, creator);
        }

        // Register a shader node implementation for a given set of implementation element names
        public void registerImplementation(List<string> nameVec, Func<ShaderNodeImpl> creator)
        {
            foreach (string name in nameVec)
            {
                _implFactory.registerClass(name, creator);
            }
        }

        // Determine if a shader node implementation has been registered for a given implementation element name
        public bool implementationRegistered(string name)
        {
            return _implFactory.classRegistered(name);
        }

        // Create the shader node implementation for a NodeGraph implementation.
        public virtual ShaderNodeImpl createShaderNodeImplForNodeGraph(NodeGraph nodegraph)
        {
            return CompoundNode.create();
        }

        // Create the shader node implementation for an Implementation implementation.
        public virtual ShaderNodeImpl createShaderNodeImplForImplementation(Implementation implementation)
        {
            return SourceCodeNode.create();
        }

        // Return a registered shader node implementation for the given nodedef.
        public virtual ShaderNodeImpl getImplementation(NodeDef nodedef, GenContext context)
        {

            InterfaceElement implElement = nodedef.getImplementation(getTarget());
            if (implElement == null)
            {
                return null;
            }

            string name = implElement.getName();

            // Check if it's created and cached already.
            ShaderNodeImpl impl = context.findNodeImplementation(name);
            if (impl != null)
            {
                return impl;
            }

            if (implElement.isA<NodeGraph>())
            {
                impl = createShaderNodeImplForNodeGraph(implElement.asA<NodeGraph>());
            }
            else if (implElement.isA<Implementation>())
            {
                Implementation implementationElement = implElement.asA<Implementation>();
                if (getColorManagementSystem() != null && getColorManagementSystem().hasImplementation(name))
                {
                    impl = getColorManagementSystem().createImplementation(name);
                }
                else
                {
                    // Try creating a new in the factory.
                    impl = _implFactory.create(name);
                }
                if (impl == null)
                {
                    impl = createShaderNodeImplForImplementation(implElement.asA<Implementation>());
                }
            }
            if (impl == null)
            {
                return null;
            }

            impl.initialize(implElement, context);

            // Cache it.
            context.addNodeImplementation(name, impl);

            return impl;
        }

        // Sets the color management system
        public void setColorManagementSystem(ColorManagementSystem colorManagementSystem)
        {
            _colorManagementSystem = colorManagementSystem;
        }

        // Returns the color management system
        public ColorManagementSystem getColorManagementSystem()
        {
            return _colorManagementSystem;
        }

        // Sets the unit system
        public void setUnitSystem(UnitSystem unitSystem)
        {
            _unitSystem = unitSystem;
        }

        // Returns the unit system
        public UnitSystem getUnitSystem()
        {
            return _unitSystem;
        }

        // Returns the type system
        public TypeSystem getTypeSystem()
        {
            return _typeSystem;
        }

        // Return the map of token substitutions used by the generator.
        public StringMap getTokenSubstitutions()
        {
            return _tokenSubstitutions;
        }

        // Register a shader graph refactoring pass.
        public void registerRefactor(ShaderGraphRefactor refactor)
        {
            _refactors.Add(refactor);
        }

        // Return the registered graph refactoring passes.
        public List<ShaderGraphRefactor> getRefactors()
        {
            return _refactors;
        }

        // Apply the default GenOptions for this generator's target.
        public virtual void applyDefaultOptions(GenOptions options)
        {
            // Base implementation sets no additional defaults.
            // Derived generators override to set target-specific defaults.
        }

        // Register type definitions from the document.
        public virtual void registerTypeDefs(Document doc)
        {
            /// Load any struct type definitions from the document.
            foreach (TypeDef mxTypeDef in doc.getTypeDefs())
            {
                string typeName = mxTypeDef.getName();
                List<Member> members = mxTypeDef.getMembers();

                // If we don't have any member children then we're not going to consider ourselves a struct.
                if (members.Count == 0)
                {
                    continue;
                }

                // Reject TypeDefs that shadow an already-registered type
                TypeDesc existingType = _typeSystem.getType(typeName);
                if ((existingType != Type.NONE && !existingType.isStruct()) || Value.hasBuiltInCreator(typeName))
                {
                    throw new ExceptionShaderGenError("TypeDef '" + typeName + "' collides with a built-in or already-registered type and cannot be registered as a struct");
                }

                List<StructMemberDesc> structMembers = new List<StructMemberDesc>();
                foreach (Member member in members)
                {
                    TypeDesc memberType = _typeSystem.getType(member.getType());
                    string memberName = member.getName();
                    string memberDefaultValue = member.getValueString();
                    structMembers.Add(new StructMemberDesc(memberType, memberName, memberDefaultValue));
                }

                _typeSystem.registerType(typeName, TypeDesc.BASETYPE_STRUCT, TypeDesc.SEMANTIC_NONE, 1, structMembers);
            }

            // Create a type syntax for all struct types loaded above.
            foreach (TypeDesc typeDesc in _typeSystem.getTypes())
            {
                if (!typeDesc.isStruct())
                {
                    continue;
                }

                string structTypeName = typeDesc.getName();
                string defaultValue = structTypeName + "( ";
                string uniformDefaultValue = EMPTY_STRING;
                string typeAlias = EMPTY_STRING;
                string typeDefinition = "struct " + structTypeName + " { ";

                List<StructMemberDesc> structMembers = typeDesc.getStructMembers();
                if (structMembers != null)
                {
                    foreach (StructMemberDesc structMember in structMembers)
                    {
                        string memberType = structMember.getType().getName();
                        string memberName = structMember.getName();
                        string memberDefaultValue = structMember.getDefaultValueStr();

                        defaultValue += memberDefaultValue + ", ";
                        typeDefinition += memberType + " " + memberName + "; ";
                    }
                }

                typeDefinition += " };";
                defaultValue += " )";

                StructTypeSyntax structTypeSyntax = _syntax.createStructSyntax(
                    structTypeName,
                    defaultValue,
                    uniformDefaultValue,
                    typeAlias,
                    typeDefinition);

                _syntax.registerTypeSyntax(typeDesc, structTypeSyntax);
            }
        }

        // Register metadata that should be exported to the generated shaders.
        // Supported metadata includes standard UI attributes like "uiname", "uifolder",
        // "uimin", "uimax", etc.
        // But it is also extendable by defining custom attributes using AttributeDefs.
        // Any AttributeDef in the given document with exportable="true" will be
        // exported as shader metadata when found on nodes during shader generation.
        // Derived shader generators may override this method to change the registration.
        // Applications must explicitly call this method before shader generation to enable
        // export of metadata.
        public virtual void registerShaderMetadata(Document doc, GenContext context)
        {
            ShaderMetadataRegistry registry = context.getUserData<ShaderMetadataRegistry>(ShaderMetadataRegistry.USER_DATA_NAME);
            if (registry == null)
            {
                registry = new ShaderMetadataRegistry();
                context.pushUserData(ShaderMetadataRegistry.USER_DATA_NAME, registry);
            }

            // Add default entries.
            ShaderMetadata[] DEFAULT_METADATA = new ShaderMetadata[]
            {
                new ShaderMetadata(ValueElement.UI_NAME_ATTRIBUTE, Type.STRING),
                new ShaderMetadata(ValueElement.UI_FOLDER_ATTRIBUTE, Type.STRING),
                new ShaderMetadata(ValueElement.UI_MIN_ATTRIBUTE, Type.NONE),
                new ShaderMetadata(ValueElement.UI_MAX_ATTRIBUTE, Type.NONE),
                new ShaderMetadata(ValueElement.UI_SOFT_MIN_ATTRIBUTE, Type.NONE),
                new ShaderMetadata(ValueElement.UI_SOFT_MAX_ATTRIBUTE, Type.NONE),
                new ShaderMetadata(ValueElement.UI_STEP_ATTRIBUTE, Type.NONE),
                new ShaderMetadata(ValueElement.UI_ADVANCED_ATTRIBUTE, Type.BOOLEAN),
                new ShaderMetadata(ValueElement.DOC_ATTRIBUTE, Type.STRING),
                new ShaderMetadata(ValueElement.UNIT_ATTRIBUTE, Type.STRING),
                new ShaderMetadata(ValueElement.COLOR_SPACE_ATTRIBUTE, Type.STRING)
            };
            foreach (ShaderMetadata data in DEFAULT_METADATA)
            {
                registry.addMetadata(data.name, data.type);
            }

            // Add entries from AttributeDefs in the document.
            List<AttributeDef> attributeDefs = doc.getAttributeDefs();
            foreach (AttributeDef def in attributeDefs)
            {
                if (def.getExportable())
                {
                    string attrName = def.getAttrName();
                    TypeDesc type = _typeSystem.getType(def.getType());
                    if (attrName.Length != 0 && type != Type.NONE)
                    {
                        registry.addMetadata(attrName, type, def.getValue());
                    }
                }
            }
        }

        // Retrieve the string used for the LightData.type member variable
        public virtual string getLightDataTypevarString()
        {
            return LIGHTDATA_TYPEVAR_STRING;
        }

        // ShaderGenerator Protected Methods
        // Protected constructor
        protected ShaderGenerator(TypeSystem typeSystem, Syntax syntax)
        {
            _typeSystem = typeSystem;
            _syntax = syntax;

            // Register all graph refactoring passes.
            registerRefactor(new NodeElisionRefactor());
            registerRefactor(new PremultipliedBsdfAddRefactor());
            registerRefactor(new DistributeLayerOverMixRefactor());
        }

        // Create a new stage in a shader.
        protected virtual ShaderStage createStage(string name, Shader shader)
        {
            return shader.createStage(name, _syntax);
        }

        // Set function name for a stage.
        protected void setFunctionName(string functionName, ShaderStage stage)
        {
            stage.setFunctionName(functionName);
        }

        // Replace tokens with identifiers according to the given substitutions map.
        protected void replaceTokens(StringMap substitutions, ShaderStage stage)
        {
            // Replace tokens in source code
            string code = stage.getSourceCode();
            tokenSubstitution(substitutions, ref code);
            stage.setSourceCode(code);

            // Replace tokens on shader interface
            for (int i = 0; i < stage._constants.size(); ++i)
            {
                replace(substitutions, stage._constants[i]);
            }
            foreach (KeyValuePair<string, VariableBlock> it in stage._uniforms)
            {
                VariableBlock uniforms = it.Value;
                for (int i = 0; i < uniforms.size(); ++i)
                {
                    replace(substitutions, uniforms[i]);
                }
            }
            foreach (KeyValuePair<string, VariableBlock> it in stage._inputs)
            {
                VariableBlock inputs = it.Value;
                for (int i = 0; i < inputs.size(); ++i)
                {
                    replace(substitutions, inputs[i]);
                }
            }
            foreach (KeyValuePair<string, VariableBlock> it in stage._outputs)
            {
                VariableBlock outputs = it.Value;
                for (int i = 0; i < outputs.size(); ++i)
                {
                    replace(substitutions, outputs[i]);
                }
            }
        }

        // Create shader variables (e.g. uniforms, inputs and outputs) for
        // nodes that require input data from the application.
        protected void createVariables(ShaderGraph graph, GenContext context, Shader shader)
        {
            Action<ShaderNode, GenContext> handler = context.getApplicationVariableHandler();
            foreach (ShaderNode node in graph.getNodes())
            {
                if (handler != null)
                {
                    handler(node, context);
                }
                node.getImplementation().createVariables(node, context, shader);
            }
        }

        // ShaderGenerator Protected Members
        protected const string T_FILE_TRANSFORM_UV = "$fileTransformUv";

        protected const string LIGHTDATA_TYPEVAR_STRING = "type";

        protected TypeSystem _typeSystem;

        protected Syntax _syntax;

        protected Factory<ShaderNodeImpl> _implFactory = new Factory<ShaderNodeImpl>();

        protected ColorManagementSystem _colorManagementSystem;

        protected UnitSystem _unitSystem;

        protected StringMap _tokenSubstitutions = new StringMap();

        protected List<ShaderGraphRefactor> _refactors = new List<ShaderGraphRefactor>();
    }

    // ShaderGenerator Functions
    public static partial class Mx
    {
        internal static void replace(StringMap substitutions, ShaderPort port)
        {
            string name = port.getName();
            tokenSubstitution(substitutions, ref name);
            port.setName(name);
            string variable = port.getVariable();
            tokenSubstitution(substitutions, ref variable);
            port.setVariable(variable);
        }
    }
}
