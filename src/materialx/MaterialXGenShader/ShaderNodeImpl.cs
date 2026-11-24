// MaterialX is Copyright Contributors to the MaterialX Project and is licensed
// under Apache-2.0. This file is a C# port of MaterialXGenShader/ShaderNodeImpl.h and ShaderNodeImpl.cpp.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using static MaterialX.Mx;

namespace MaterialX
{
    // ShaderNodeImpl Definition
    // Class handling the shader generation implementation for a node.
    // Responsible for emitting the function definition and function call
    // that is the node implementation.
    public class ShaderNodeImpl
    {
        // ShaderNodeImpl Public Methods
        // Initialize with the given implementation element.
        // Initialization must set the name and hash for the implementation,
        // as well as any other data needed to emit code for the node.
        public virtual void initialize(InterfaceElement element, GenContext context)
        {
            // Store name
            _name = element.getName();

            // By default use the implementation name as hash to make it unique.
            // Derived classes can override this to create other hashes,
            // e.g. to share the same hash between nodes that can share
            // the same function definition.
            _hash = stringHash(_name);
        }

        // Return the name of this implementation.
        public string getName()
        {
            return _name;
        }

        // Return a hash for this implementation.
        // The hash should correspond to the function signature generated for the node,
        // and can be used to compare implementations, e.g. to query if an identical
        // function has already been emitted during shader generation.
        public ulong getHash()
        {
            return _hash;
        }

        // Add additional inputs on a node.
        public virtual void addInputs(ShaderNode node, GenContext context)
        {
        }

        // Set values for additional inputs on a node.
        public virtual void setValues(Node node, ShaderNode shaderNode, GenContext context)
        {
        }

        // Add additional classifications on a node.
        public virtual void addClassification(ShaderNode node)
        {
        }

        // Create shader variables needed for the implementation of this node (e.g. uniforms, inputs and outputs).
        // Used if the node requires input data from the application.
        public virtual void createVariables(ShaderNode node, GenContext context, Shader shader)
        {
        }

        // Emit function definition for the given node instance.
        public virtual void emitFunctionDefinition(ShaderNode node, GenContext context, ShaderStage stage)
        {
        }

        // Emit the function call or inline source code for given node instance in the given context.
        public virtual void emitFunctionCall(ShaderNode node, GenContext context, ShaderStage stage)
        {
        }

        // Emit declaration and initialization of output variables to use in a function call.
        public virtual void emitOutputVariables(ShaderNode node, GenContext context, ShaderStage stage)
        {
            // Default implementation of output variable declaration.
            // Initialize variables to their type default value.
            ShaderGenerator shadergen = context.getShaderGenerator();
            for (int i = 0; i < node.numOutputs(); ++i)
            {
                shadergen.emitLineBegin(stage);
                shadergen.emitOutput(node.getOutput(i), true, true, context, stage);
                shadergen.emitLineEnd(stage);
            }
        }

        // Return a pointer to the graph if this implementation is using a graph,
        // or returns nullptr otherwise.
        public virtual ShaderGraph getGraph()
        {
            return null;
        }

        // Returns true if an input is editable by users.
        // Editable inputs are allowed to be published as shader uniforms
        // and hence must be presentable in a user interface.
        // By default all inputs are considered to be editable.
        public virtual bool isEditable(ShaderInput input)
        {
            return true;
        }

        // Returns true if a graph input is accessible by users.
        // Accessible inputs are allowed to be published as shader uniforms
        // and hence must be presentable in a user interface.
        // By default all graph inputs are considered to be accessible.
        public virtual bool isEditable(ShaderGraphInputSocket input)
        {
            return true;
        }

        // ShaderNodeImpl Protected Methods
        // Protected constructor
        protected ShaderNodeImpl()
        {
            _name = EMPTY_STRING;
            _hash = 0;
        }

        // Returns true if the first output of the node is a closure
        // This is used by SourceCodeNode and CompoundNode to generate
        // the appropriate shader code.
        protected bool nodeOutputIsClosure(ShaderNode node)
        {
            List<ShaderOutput> outputs = node.getOutputs();
            if (outputs.Count == 0)
            {
                // This should never happen as we auto populate the default 'out' output based on the
                // node type if no output is present.
                throw new ExceptionShaderGenError("Node has no outputs defined'");
            }
            return outputs[0].getType().isClosure();
        }

        // ShaderNodeImpl Protected Members
        protected string _name = "";

        protected ulong _hash;
    }

    // NopNode Definition
    // A no operation node, to be used for organizational nodes that has no code to execute.
    public class NopNode : ShaderNodeImpl
    {
        // NopNode Public Methods
        public static ShaderNodeImpl create()
        {
            return new NopNode();
        }
    }
}
