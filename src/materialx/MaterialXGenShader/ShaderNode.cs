// MaterialX is Copyright Contributors to the MaterialX Project and is licensed
// under Apache-2.0. This file is a C# port of MaterialXGenShader/ShaderNode.h and ShaderNode.cpp.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using static MaterialX.Mx;

namespace MaterialX
{
    // ShaderMetadata Definition
    // Metadata to be exported to generated shader.
    public class ShaderMetadata
    {
        // ShaderMetadata Public Members
        public string name = "";
        public TypeDesc type;
        public Value value;

        // ShaderMetadata Public Methods
        public ShaderMetadata(string n, TypeDesc t, Value v = null)
        {
            name = n;
            type = t;
            value = v;
        }
    }

    // ShaderMetadataRegistry Definition
    // A registry for metadata that will be exported to the generated shader
    // if found on nodes and inputs during shader generation.
    public class ShaderMetadataRegistry : GenUserData
    {
        // ShaderMetadataRegistry Public Members
        public const string USER_DATA_NAME = "ShaderMetadataRegistry";

        // ShaderMetadataRegistry Public Methods
        // Add a new metadata entry to the registry.
        // The entry contains the name and data type
        // for the metadata.
        public void addMetadata(string name, TypeDesc type, Value value = null)
        {
            if (!_entryIndex.ContainsKey(name))
            {
                _entryIndex[name] = _entries.Count;
                _entries.Add(new ShaderMetadata(name, type, value));
            }
        }

        // Return the metadata registered for the given name, or nullptr
        // if no such entry is found.
        public ShaderMetadata findMetadata(string name)
        {
            int found;
            return _entryIndex.TryGetValue(name, out found) ? _entries[found] : null;
        }

        // Return all entries in the registry.
        public List<ShaderMetadata> getAllMetadata()
        {
            return _entries;
        }

        // Clear all entries in the registry.
        public void clear()
        {
            _entryIndex.Clear();
            _entries.Clear();
        }

        // ShaderMetadataRegistry Protected Members
        protected List<ShaderMetadata> _entries = new List<ShaderMetadata>();

        protected Dictionary<string, int> _entryIndex = new Dictionary<string, int>();
    }

    // ShaderPortFlag Definition
    // Flags set on shader ports.
    public class ShaderPortFlag
    {
        // ShaderPortFlag Public Members
        public static readonly uint UNIFORM = 1u << 0;

        public static readonly uint EMITTED = 1u << 1;

        public static readonly uint BIND_INPUT = 1u << 2;

        public static readonly uint AUTHORED_VALUE = 1u << 3;
    }

    // ShaderPort Definition
    // An input or output port on a ShaderNode
    public class ShaderPort
    {
        // ShaderPort Public Methods
        // Constructor.
        public ShaderPort(ShaderNode node, TypeDesc type, string name, Value value = null)
        {
            _node = node;
            _type = type;
            _name = name;
            _variable = name;
            _value = value;
            _flags = 0;
        }

        // Return a shared pointer instance of this object.
        public ShaderPort getSelf()
        {
            return this;
        }

        // Return the node this port belongs to.
        public ShaderNode getNode()
        {
            return _node;
        }

        // Set the data type for this port.
        public void setType(TypeDesc type)
        {
            _type = type;
        }

        // Return the data type for this port.
        public TypeDesc getType()
        {
            return _type;
        }

        // Set the name of this port.
        public void setName(string name)
        {
            _name = name;
        }

        // Return the name of this port.
        public string getName()
        {
            return _name;
        }

        // Return the name of this port.
        public string getFullName()
        {
            return (_node.getName() + "_" + _name);
        }

        // Set the variable name of this port.
        public void setVariable(string name)
        {
            _variable = name;
        }

        // Return the variable name of this port.
        public string getVariable()
        {
            return _variable;
        }

        // Set the variable semantic of this port.
        public void setSemantic(string semantic)
        {
            _semantic = semantic;
        }

        // Return the variable semantic of this port.
        public string getSemantic()
        {
            return _semantic;
        }

        // Set a value on this port.
        public void setValue(Value value, bool authoredValue = true)
        {
            _value = value;
            setFlag(ShaderPortFlag.AUTHORED_VALUE, authoredValue);
        }

        // Return the value set on this port.
        public Value getValue()
        {
            return _value;
        }

        // Return the value set on this port as a string, or an empty string if there is no value.
        public string getValueString()
        {
            return getValue() != null ? getValue().getValueString() : EMPTY_STRING;
        }

        // Set a source color space for the value on this port.
        public void setColorSpace(string colorspace)
        {
            _colorspace = colorspace;
        }

        // Return the source color space for the value on this port.
        public string getColorSpace()
        {
            return _colorspace;
        }

        // Set a unit type for the value on this port.
        public void setUnit(string unit)
        {
            _unit = unit;
        }

        // Return the unit type for the value on this port.
        public string getUnit()
        {
            return _unit;
        }

        // Set geomprop name if the input has a default
        // geomprop to be assigned when it is unconnected.
        public void setGeomProp(string geomprop)
        {
            _geomprop = geomprop;
        }

        // Get geomprop name.
        public string getGeomProp()
        {
            return _geomprop;
        }

        // Set the path to this port.
        public void setPath(string path)
        {
            _path = path;
        }

        // Return the path to this port.
        public string getPath()
        {
            return _path;
        }

        // Set flags on this port.
        public void setFlags(uint flags)
        {
            _flags = flags;
        }

        // Return flags set on this port.
        public uint getFlags()
        {
            return _flags;
        }

        // Set the on|off state of a given flag.
        public void setFlag(uint flag, bool value)
        {
            _flags = value ? (_flags | flag) : (_flags & ~flag);
        }

        // Return the on|off state of a given flag.
        public bool getFlag(uint flag)
        {
            return ((_flags & flag) != 0);
        }

        // Set the uniform flag this port to true.
        public void setUniform()
        {
            _flags |= ShaderPortFlag.UNIFORM;
        }

        // Return the uniform flag on this port.
        public bool isUniform()
        {
            return (_flags & ShaderPortFlag.UNIFORM) != 0;
        }

        // Set the emitted state on this port to true.
        public void setEmitted()
        {
            _flags |= ShaderPortFlag.EMITTED;
        }

        // Return the emitted state of this port.
        public bool isEmitted()
        {
            return (_flags & ShaderPortFlag.EMITTED) != 0;
        }

        // Set the bind input state on this port to true.
        public void setBindInput()
        {
            _flags |= ShaderPortFlag.BIND_INPUT;
        }

        // Return the emitted state of this port.
        public bool isBindInput()
        {
            return (_flags & ShaderPortFlag.BIND_INPUT) != 0;
        }

        // Has the value been overridden.
        public bool hasAuthoredValue()
        {
            return (_flags & ShaderPortFlag.AUTHORED_VALUE) != 0;
        }

        // Set the metadata vector.
        public void setMetadata(List<ShaderMetadata> metadata)
        {
            _metadata = metadata;
        }

        // Get the metadata vector.
        public List<ShaderMetadata> getMetadata()
        {
            return _metadata;
        }

        // ShaderPort Protected Members
        protected ShaderNode _node;

        protected TypeDesc _type;

        protected string _name = "";

        protected string _path = "";

        protected string _semantic = "";

        protected string _variable = "";

        protected Value _value;

        protected string _unit = "";

        protected string _colorspace = "";

        protected string _geomprop = "";

        protected List<ShaderMetadata> _metadata;

        protected uint _flags;
    }

    // ShaderInput Definition
    // An input on a ShaderNode
    public class ShaderInput : ShaderPort
    {
        // ShaderInput Public Methods
        public ShaderInput(ShaderNode node, TypeDesc type, string name) : base(node, type, name)
        {
            _connection = null;
        }

        // Return a connection to an upstream node output,
        // or nullptr if not connected.
        public ShaderOutput getConnection()
        {
            return _connection;
        }

        // Make a connection from the given source output to this input.
        public void makeConnection(ShaderOutput src)
        {
            // Make sure this is a new connection.
            if (src != _connection)
            {
                // Break the old connection.
                breakConnection();
                if (src != null)
                {
                    // Make the new connection.
                    if (src.getNode() == getNode() && !getNode().isAGraph())
                    {
                        throw new ExceptionShaderGenError(
                            "Tried to create looping connection on node " + getNode().getName()
                                + " from output: " + src.getFullName() + " to input: " + getFullName());
                    }

                    _connection = src;
                    src._connections.Add(this);
                }
            }
        }

        // Break the connection to this input.
        public void breakConnection()
        {
            if (_connection != null)
            {
                // Find and erase this input from the connected output's connection vector.
                List<ShaderInput> connected = _connection._connections;
                int it = connected.IndexOf(this);
                if (it != -1)
                {
                    connected.RemoveAt(it);
                }
                // Clear the connection.
                _connection = null;
            }
        }

        // Return the sibling node connected upstream,
        // or nullptr if there is no sibling upstream.
        public ShaderNode getConnectedSibling()
        {
            if (_connection != null && _connection.getNode().getParent() == _node.getParent())
            {
                return _connection.getNode();
            }
            return null;
        }

        // ShaderInput Protected Members
        protected ShaderOutput _connection;

        protected string _channels = "";
    }

    // ShaderOutput Definition
    // An output on a ShaderNode
    public class ShaderOutput : ShaderPort
    {
        // ShaderOutput Public Methods
        public ShaderOutput(ShaderNode node, TypeDesc type, string name) : base(node, type, name)
        {
        }

        // Return a set of connections to downstream node inputs,
        // empty if not connected.
        public List<ShaderInput> getConnections()
        {
            return _connections;
        }

        // Make a connection from this output to the given input
        public void makeConnection(ShaderInput dst)
        {
            dst.makeConnection(this);
        }

        // Break a connection from this output to the given input
        public void breakConnection(ShaderInput dst)
        {
            if (!_connections.Contains(dst))
            {
                throw new ExceptionShaderGenError(
                    "Cannot break non-existent connection from output: " + getFullName() + " to input: " + dst.getFullName());
            }
            dst.breakConnection();
        }

        // Break all connections from this output
        public void breakConnections()
        {
            List<ShaderInput> inputVec = new List<ShaderInput>(_connections);
            foreach (ShaderInput input in inputVec)
            {
                input.breakConnection();
            }

            if (_connections.Count != 0)
            {
                throw new ExceptionShaderGenError("Number of output connections not broken properly'" + Mx.to_string(_connections.Count) +
                                              " for output: " + getFullName());
            }
        }

        // ShaderOutput Protected Members
        protected internal List<ShaderInput> _connections = new List<ShaderInput>();
    }

    // ShaderNode Definition
    // Class representing a node in the shader generation DAG
    public partial class ShaderNode
    {
        // ShaderNode Public Types
        // Flags for classifying nodes into different categories.
        public static class Classification
        {
            // Any node that outputs floats, colors, vectors, etc.
            public const uint TEXTURE = 1u << 0;
            // Any node that represents light integration
            public const uint CLOSURE = 1u << 1;
            // Any node that outputs a shader
            public const uint SHADER = 1u << 2;
            // Any node that outputs a material
            public const uint MATERIAL = 1u << 3;
            // A file texture node
            public const uint FILETEXTURE = 1u << 4;
            // A conditional node
            public const uint CONDITIONAL = 1u << 5;
            // A constant node
            public const uint CONSTANT = 1u << 6;
            // A BSDF node
            public const uint BSDF = 1u << 7;
            // A reflection BSDF node
            public const uint BSDF_R = 1u << 8;
            // A transmission BSDF node
            public const uint BSDF_T = 1u << 9;
            // A EDF node
            public const uint EDF = 1u << 10;
            // A VDF node
            public const uint VDF = 1u << 11;
            // A node for vertical layering of other closure nodes
            public const uint LAYER = 1u << 12;
            // A node for mixing of other closure nodes
            public const uint MIX = 1u << 13;
            // A surface shader node
            public const uint SURFACE = 1u << 14;
            // A volume shader node
            public const uint VOLUME = 1u << 15;
            // A light shader node
            public const uint LIGHT = 1u << 16;
            // An unlit surface shader node
            public const uint UNLIT = 1u << 17;
            // Can be sampled in 2D (uv space)
            public const uint SAMPLE2D = 1u << 18;
            // Can be sampled in 3D (position)
            public const uint SAMPLE3D = 1u << 19;
            // Geometric input
            public const uint GEOMETRIC = 1u << 20;
            // A dot node
            public const uint DOT = 1u << 21;
        }

        // ShaderNode Public Members
        public static readonly ShaderNode NONE = createEmptyNode();

        public const string CONSTANT = "constant";

        public const string DOT = "dot";

        public const string IMAGE = "image";

        public const string SURFACESHADER = "surfaceshader";

        public const string BACKSURFACESHADER = "backsurfaceshader";

        public const string BSDF_R = "R";

        public const string BSDF_T = "T";

        public const string TEXTURE2D_GROUPNAME = "texture2d";

        public const string TEXTURE3D_GROUPNAME = "texture3d";

        public const string PROCEDURAL2D_GROUPNAME = "procedural2d";

        public const string PROCEDURAL3D_GROUPNAME = "procedural3d";

        public const string GEOMETRIC_GROUPNAME = "geometric";

        // ShaderNode Public Methods
        // Constructor.
        public ShaderNode(ShaderGraph parent, string name)
        {
            _parent = parent;
            _name = name;
            _uniqueId = name;
            _classification = 0;
            _impl = null;
        }

        // Create a new node from a nodedef.
        public static ShaderNode create(ShaderGraph parent, string name, NodeDef nodeDef, GenContext context)
        {

            ShaderNode newNode = new ShaderNode(parent, name);

            ShaderGenerator shadergen = context.getShaderGenerator();

            // Find the implementation for this nodedef
            newNode._impl = shadergen.getImplementation(nodeDef, context);
            if (newNode._impl == null)
            {
                throw new ExceptionShaderGenError("Could not find a matching implementation for node '" + nodeDef.getNodeString() +
                                              "' matching target '" + shadergen.getTarget() + "'");
            }

            // Create interface from nodedef
            foreach (ValueElement port in nodeDef.getActiveValueElements())
            {
                TypeDesc portType = context.getTypeDesc(port.getType());
                if (port.isA<Output>())
                {
                    newNode.addOutput(port.getName(), portType);
                }
                else if (port.isA<Input>())
                {
                    ShaderInput input;
                    string portValue = port.getResolvedValueString();
                    (TypeDesc, Value) enumResult = default;
                    string enumNames = port.getAttribute(ValueElement.ENUM_ATTRIBUTE);
                    if (context.getShaderGenerator().getSyntax().remapEnumeration(portValue, portType, enumNames, ref enumResult))
                    {
                        input = newNode.addInput(port.getName(), enumResult.Item1);
                        input.setValue(enumResult.Item2, false);
                    }
                    else
                    {
                        input = newNode.addInput(port.getName(), portType);
                        if (portValue.Length != 0)
                        {
                            input.setValue(port.getResolvedValue(), false);
                        }
                    }
                    if (port.getIsUniform())
                    {
                        input.setUniform();
                    }
                }
            }

            // Add any additional inputs required by the implementation
            newNode.getImplementation().addInputs(newNode, context);

            // Add a default output if needed
            if (newNode.numOutputs() == 0)
            {
                newNode.addOutput("out", context.getTypeDesc(nodeDef.getType()));
            }

            string nodeDefName = nodeDef.getName();
            string groupName = nodeDef.getNodeGroup();

            //
            // Set node classification, defaulting to texture node
            //
            newNode._classification = Classification.TEXTURE;

            // First, check for specific output types
            ShaderOutput primaryOutput = newNode.getOutput();
            if (primaryOutput.getType() == Type.MATERIAL)
            {
                newNode._classification = Classification.MATERIAL;
            }
            else if (primaryOutput.getType() == Type.SURFACESHADER)
            {
                if (nodeDefName == "ND_surface_unlit")
                {
                    newNode._classification = Classification.SHADER | Classification.SURFACE | Classification.UNLIT;
                }
                else
                {
                    newNode._classification = Classification.SHADER | Classification.SURFACE | Classification.CLOSURE;
                }
            }
            else if (primaryOutput.getType() == Type.VOLUMESHADER)
            {
                newNode._classification = Classification.SHADER | Classification.VOLUME | Classification.CLOSURE;
            }
            else if (primaryOutput.getType() == Type.LIGHTSHADER)
            {
                newNode._classification = Classification.LIGHT | Classification.SHADER | Classification.CLOSURE;
            }
            else if (primaryOutput.getType() == Type.BSDF)
            {
                newNode._classification = Classification.BSDF | Classification.CLOSURE;

                // Add additional classifications for BSDF reflection and/or transmission.
                string bsdfType = nodeDef.getAttribute("bsdf");
                if (bsdfType == BSDF_R)
                {
                    newNode._classification |= Classification.BSDF_R;
                }
                else if (bsdfType == BSDF_T)
                {
                    newNode._classification |= Classification.BSDF_T;
                }
                else
                {
                    newNode._classification |= (Classification.BSDF_R | Classification.BSDF_T);
                }

                // Check specifically for the vertical layering node
                if (nodeDefName == "ND_layer_bsdf" || nodeDefName == "ND_layer_vdf")
                {
                    newNode._classification |= Classification.LAYER;
                }

                // Check specifically for the mix node
                if (nodeDefName == "ND_mix_bsdf")
                {
                    newNode._classification |= Classification.MIX;
                }
            }
            else if (primaryOutput.getType() == Type.EDF)
            {
                newNode._classification = Classification.EDF | Classification.CLOSURE;
                if (nodeDefName == "ND_mix_edf")
                {
                    newNode._classification |= Classification.MIX;
                }
            }
            else if (primaryOutput.getType() == Type.VDF)
            {
                newNode._classification = Classification.VDF | Classification.CLOSURE;
                if (nodeDefName == "ND_mix_vdf")
                {
                    newNode._classification |= Classification.MIX;
                }
            }
            // Second, check for specific nodes types
            else if (nodeDef.getNodeString() == CONSTANT)
            {
                newNode._classification = Classification.TEXTURE | Classification.CONSTANT;
            }
            else if (nodeDef.getNodeString() == DOT)
            {
                newNode._classification = Classification.TEXTURE | Classification.DOT;
            }
            // Third, check for file texture classification by group name
            else if (groupName == TEXTURE2D_GROUPNAME || groupName == TEXTURE3D_GROUPNAME)
            {
                newNode._classification = Classification.TEXTURE | Classification.FILETEXTURE;
            }

            // Add in classification based on group name
            if (groupName == TEXTURE2D_GROUPNAME || groupName == PROCEDURAL2D_GROUPNAME)
            {
                newNode._classification |= Classification.SAMPLE2D;
            }
            else if (groupName == TEXTURE3D_GROUPNAME || groupName == PROCEDURAL3D_GROUPNAME)
            {
                newNode._classification |= Classification.SAMPLE3D;
            }
            else if (groupName == GEOMETRIC_GROUPNAME)
            {
                newNode._classification |= Classification.GEOMETRIC;
            }

            // Create any metadata.
            newNode.createMetadata(nodeDef, context);

            return newNode;
        }

        // Create a new node from a node implementation.
        public static ShaderNode create(ShaderGraph parent, string name, ShaderNodeImpl impl, uint classification = Classification.TEXTURE)
        {
            ShaderNode newNode = new ShaderNode(parent, name);
            newNode._impl = impl;
            newNode._classification = classification;
            return newNode;
        }

        // Return true if this node is a graph.
        public virtual bool isAGraph()
        {
            return false;
        }

        // Return the parent graph that owns this node.
        // If this node is a root graph it has no parent
        // and nullptr will be returned.
        public ShaderGraph getParent()
        {
            return _parent;
        }

        // Set classification bits for this node,
        // replacing any previous set bits.
        public void setClassification(uint c)
        {
            _classification = c;
        }

        // Get classification bits set for this node.
        public uint getClassification()
        {
            return _classification;
        }

        // Add classification bits to this node.
        public void addClassification(uint c)
        {
            _classification |= c;
        }

        // Return true if this node matches the given classification.
        public bool hasClassification(uint c)
        {
            return (_classification & c) == c;
        }

        // Return the name of this node.
        public string getName()
        {
            return _name;
        }

        // Return the unique identifier for this node, used as its key
        // in the parent graph's node map.
        public string getUniqueId()
        {
            return _uniqueId;
        }

        // Return the implementation used for this node.
        public ShaderNodeImpl getImplementation()
        {
            return _impl;
        }

        // Initialize this shader node with all required data
        // from the given node and nodedef.
        public void initialize(Node node, NodeDef nodeDef, GenContext context)
        {
            // Copy input values from the given node
            foreach (Input nodeInput in node.getActiveInputs())
            {
                ShaderInput input = getInput(nodeInput.getName());
                ValueElement nodeDefInput = nodeDef.getActiveValueElement(nodeInput.getName());
                if (input != null && nodeDefInput != null)
                {
                    Value portValue = nodeInput.getResolvedValue();
                    if (portValue == null)
                    {
                        Input interfaceInput = nodeInput.getInterfaceInput();
                        if (interfaceInput != null)
                        {
                            portValue = interfaceInput.getResolvedValue();
                        }
                    }
                    string valueString = portValue != null ? portValue.getValueString() : EMPTY_STRING;
                    if (valueString.Length != 0)
                    {
                        // We explicitly check the valueString is not empty before checking the enumeration,
                        // because otherwise the enumeration value would always return nullptr
                        (TypeDesc, Value) enumResult = default;
                        string enumNames = nodeDefInput.getAttribute(ValueElement.ENUM_ATTRIBUTE);
                        TypeDesc type = context.getTypeDesc(nodeDefInput.getType());
                        if (context.getShaderGenerator().getSyntax().remapEnumeration(valueString, type, enumNames, ref enumResult))
                        {
                            input.setValue(enumResult.Item2);
                        }
                        else
                        {
                            input.setValue(portValue);
                        }
                    }
                }
            }

            // Set implementation specific values.
            if (_impl != null)
            {
                _impl.setValues(node, this, context);
            }

            // Set element paths for children on the node
            foreach (ValueElement nodeValue in node.getActiveValueElements())
            {
                ShaderInput input = getInput(nodeValue.getName());
                if (input != null)
                {
                    string path = nodeValue.getNamePath();
                    Input nodeInput = nodeValue.asA<Input>();
                    if (nodeInput != null)
                    {
                        Input interfaceInput = nodeInput.getInterfaceInput();
                        if (interfaceInput != null)
                        {
                            path = interfaceInput.getNamePath();
                        }
                    }
                    input.setPath(path);
                }
            }

            // Set element paths based on the node definition. Note that these
            // paths don't actually exist at time of shader generation since there
            // are no inputs specified on the node itself
            //
            string nodePath = node.getNamePath();
            foreach (Input nodeInput in nodeDef.getActiveInputs())
            {
                ShaderInput input = getInput(nodeInput.getName());
                if (input != null && input.getPath().Length == 0)
                {
                    input.setPath(nodePath + NAME_PATH_SEPARATOR + nodeInput.getName());
                }
            }
        }

        // Add inputs/outputs
        public ShaderInput addInput(string name, TypeDesc type)
        {
            if (getInput(name) != null)
            {
                throw new ExceptionShaderGenError("An input named '" + name + "' already exists on node '" + _name + "'");
            }

            ShaderInput input = new ShaderInput(this, type, name);
            _inputMap[name] = input;
            _inputOrder.Add(input);

            return input;
        }

        public ShaderOutput addOutput(string name, TypeDesc type)
        {
            if (getOutput(name) != null)
            {
                throw new ExceptionShaderGenError("An output named '" + name + "' already exists on node '" + _name + "'");
            }

            ShaderOutput output = new ShaderOutput(this, type, name);
            _outputMap[name] = output;
            _outputOrder.Add(output);

            return output;
        }

        // Get number of inputs/outputs
        public int numInputs()
        {
            return _inputOrder.Count;
        }

        public int numOutputs()
        {
            return _outputOrder.Count;
        }

        // Get inputs/outputs by index
        public ShaderInput getInput(int index)
        {
            return _inputOrder[index];
        }

        public ShaderOutput getOutput(int index = 0)
        {
            return _outputOrder[index];
        }

        // Get inputs/outputs by name
        public ShaderInput getInput(string name)
        {
            ShaderInput found;
            return _inputMap.TryGetValue(name, out found) ? found : null;
        }

        public ShaderOutput getOutput(string name)
        {
            ShaderOutput found;
            return _outputMap.TryGetValue(name, out found) ? found : null;
        }

        // Get vector of inputs/outputs
        public List<ShaderInput> getInputs()
        {
            return _inputOrder;
        }

        public List<ShaderOutput> getOutputs()
        {
            return _outputOrder;
        }

        // Set the metadata vector.
        public void setMetadata(List<ShaderMetadata> metadata)
        {
            _metadata = metadata;
        }

        // Get the metadata vector.
        public List<ShaderMetadata> getMetadata()
        {
            return _metadata;
        }

        // Returns true if an input is editable by users.
        // Editable inputs are allowed to be published as shader uniforms
        // and hence must be presentable in a user interface.
        public bool isEditable(ShaderInput input)
        {
            return (_impl == null || _impl.isEditable(input));
        }

        // Returns true if a graph input is accessible by users.
        // Editable inputs are allowed to be published as shader uniforms
        // and hence must be presentable in a user interface.
        public bool isEditable(ShaderGraphInputSocket input)
        {
            return (_impl == null || _impl.isEditable(input));
        }

        // ShaderNode Protected Methods
        // Create metadata from the nodedef according to registered metadata.
        protected void createMetadata(NodeDef nodeDef, GenContext context)
        {
            ShaderMetadataRegistry registry = context.getUserData<ShaderMetadataRegistry>(ShaderMetadataRegistry.USER_DATA_NAME);
            if (!(registry != null && registry.getAllMetadata().Count != 0))
            {
                // Early out if no metadata is registered.
                return;
            }

            // Set metadata on the node according to the nodedef attributes.
            List<ShaderMetadata> nodeMetadataStorage = getMetadata();
            foreach (string nodedefAttr in nodeDef.getAttributeNames())
            {
                ShaderMetadata metadataEntry = registry.findMetadata(nodedefAttr);
                if (metadataEntry != null)
                {
                    string attrValue = nodeDef.getAttribute(nodedefAttr);
                    if (attrValue.Length != 0)
                    {
                        Value value = metadataEntry.type.createValueFromStrings(attrValue);
                        if (value == null)
                        {
                            value = metadataEntry.value;
                        }
                        if (value != null)
                        {
                            if (nodeMetadataStorage == null)
                            {
                                nodeMetadataStorage = new List<ShaderMetadata>();
                                setMetadata(nodeMetadataStorage);
                            }
                            nodeMetadataStorage.Add(new ShaderMetadata(metadataEntry.name, metadataEntry.type, value));
                        }
                    }
                }
            }

            // Set metadata on inputs according to attributes on the nodedef's inputs
            foreach (ValueElement nodedefPort in nodeDef.getActiveValueElements())
            {
                ShaderInput input = getInput(nodedefPort.getName());
                if (input != null)
                {
                    List<ShaderMetadata> inputMetadataStorage = input.getMetadata();

                    foreach (string nodedefPortAttr in nodedefPort.getAttributeNames())
                    {
                        ShaderMetadata metadataEntry = registry.findMetadata(nodedefPortAttr);
                        if (metadataEntry != null)
                        {
                            string attrValue = nodedefPort.getAttribute(nodedefPortAttr);
                            if (attrValue.Length != 0)
                            {
                                TypeDesc type = metadataEntry.type != Type.NONE ? metadataEntry.type : input.getType();
                                Value value = type.createValueFromStrings(attrValue);
                                if (value == null)
                                {
                                    value = metadataEntry.value;
                                }
                                if (value != null)
                                {
                                    if (inputMetadataStorage == null)
                                    {
                                        inputMetadataStorage = new List<ShaderMetadata>();
                                        input.setMetadata(inputMetadataStorage);
                                    }
                                    inputMetadataStorage.Add(new ShaderMetadata(metadataEntry.name, type, value));
                                }
                            }
                        }
                    }
                }
            }
        }

        // ShaderNode Protected Members
        protected ShaderGraph _parent;

        protected string _name = "";

        protected internal string _uniqueId = "";

        protected uint _classification;

        protected Dictionary<string, ShaderInput> _inputMap = new Dictionary<string, ShaderInput>();

        protected List<ShaderInput> _inputOrder = new List<ShaderInput>();

        protected Dictionary<string, ShaderOutput> _outputMap = new Dictionary<string, ShaderOutput>();

        protected List<ShaderOutput> _outputOrder = new List<ShaderOutput>();

        protected ShaderNodeImpl _impl;

        protected List<ShaderMetadata> _metadata;
    }

    // ShaderNode Functions
    public static partial class Mx
    {
        internal static ShaderNode createEmptyNode()
        {
            return new ShaderNode(null, "");
        }
    }
}
