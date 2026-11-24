// MaterialX is Copyright Contributors to the MaterialX Project and is licensed
// under Apache-2.0. This file is a C# port of MaterialXGenShader/Shader.h and Shader.cpp.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using static MaterialX.Mx;

namespace MaterialX
{
    // Shader Definition
    // Class containing all data needed during shader generation.
    // After generation is completed it will contain the resulting source code
    // emitted by shader generators.
    //
    // The class contains a default implementation using a single shader stage.
    // Derived shaders can override this, as well as overriding all methods
    // that add code to the shader.
    //
    public partial class Shader
    {
        // Shader Public Methods
        // Constructor
        public Shader(string name, ShaderGraph graph)
        {
            _name = name;
            _graph = graph;
        }

        // Return the shader name
        public string getName()
        {
            return _name;
        }

        // Return the number of shader stages for this shader.
        public int numStages()
        {
            return _stages.Count;
        }

        // Return a stage by index.
        public ShaderStage getStage(int index)
        {
            return _stages[index];
        }

        // Return if stage exists.
        public bool hasStage(string name)
        {
            ShaderStage found;
            return _stagesMap.TryGetValue(name, out found);
        }

        // Return a stage by name.
        public ShaderStage getStage(string name)
        {
            ShaderStage found;
            if (!_stagesMap.TryGetValue(name, out found))
            {
                throw new ExceptionShaderGenError("Stage '" + name + "' doesn't exist in shader '" + getName() + "'");
            }
            return found;
        }

        // Return true if the shader has a given named attribute.
        public bool hasAttribute(string attrib)
        {
            return _attributeMap.ContainsKey(attrib);
        }

        // Return the value for a named attribute,
        // or nullptr if no such attribute is found.
        public Value getAttribute(string attrib)
        {
            Value found;
            return _attributeMap.TryGetValue(attrib, out found) ? found : null;
        }

        // Set a value attribute on the shader.
        public void setAttribute(string attrib, Value value)
        {
            _attributeMap[attrib] = value;
        }

        // Set a flag attribute on the shader.
        public void setAttribute(string attrib)
        {
            _attributeMap[attrib] = Value.createValue<bool>(true);
        }

        // Return the shader graph.
        public ShaderGraph getGraph()
        {
            return _graph;
        }

        // Return true if this shader matches the given classification.
        public bool hasClassification(uint c)
        {
            return _graph.hasClassification(c);
        }

        // Set the shader source code for a given shader stage.
        public void setSourceCode(string code, string stage = Stage.PIXEL)
        {
            getStage(stage).setSourceCode(code);
        }

        // Return the shader source code for a given shader stage.
        public string getSourceCode(string stage = Stage.PIXEL)
        {
            return getStage(stage).getSourceCode();
        }

        // Shader Protected Methods
        // Create a new stage in the shader.
        protected internal ShaderStage createStage(string name, Syntax syntax)
        {
            ShaderStage found;
            if (_stagesMap.TryGetValue(name, out found))
            {
                throw new ExceptionShaderGenError("Stage '" + name + "' already exist in shader '" + getName() + "'");
            }

            ShaderStage s = new ShaderStage(name, syntax);
            _stagesMap[name] = s;
            _stages.Add(s);

            return s;
        }

        // Shader Protected Members
        protected string _name = "";

        protected ShaderGraph _graph;

        protected Dictionary<string, ShaderStage> _stagesMap = new Dictionary<string, ShaderStage>();

        protected List<ShaderStage> _stages = new List<ShaderStage>();

        protected Dictionary<string, Value> _attributeMap = new Dictionary<string, Value>();
    }

}
