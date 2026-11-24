// MaterialX is Copyright Contributors to the MaterialX Project and is licensed
// under Apache-2.0. This file is a C# port of MaterialXGenShader/ShaderGraph.h and ShaderGraph.cpp.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using static MaterialX.Mx;

namespace MaterialX
{
    // ShaderGraph Definition
    // Class representing a graph (DAG) for shader generation
    public class ShaderGraph : ShaderNode
    {
        // ShaderGraph Public Methods
        // Constructor.
        public ShaderGraph(ShaderGraph parent, string name, Document document, GenContext context) : base(parent, name)
        {
            _document = document;

            context.getShaderGenerator().getSyntax().makeIdentifier(ref _name, getIdentifierMap());
        }

        // Create a new shader graph from an element.
        // Supported elements are outputs and shader nodes.
        public static ShaderGraph create(ShaderGraph parent, string name, Element element, GenContext context)
        {
            ShaderGraph graph = null;
            Element root = null;

            if (element.isA<Output>())
            {
                Output output = element.asA<Output>();
                Element outputParent = output.getParent();

                InterfaceElement @interface = null;
                if (outputParent.isA<NodeGraph>())
                {
                    // A nodegraph output.
                    NodeGraph nodeGraph = outputParent.asA<NodeGraph>();
                    NodeDef nodeDef = nodeGraph.getNodeDef();
                    if (nodeDef != null)
                    {
                        @interface = nodeDef;
                    }
                    else
                    {
                        @interface = nodeGraph;
                    }
                }
                else if (outputParent.isA<Document>())
                {
                    // A free floating output.
                    outputParent = output.getConnectedNode();
                    @interface = outputParent != null ? outputParent.asA<InterfaceElement>() : null;
                }
                if (@interface == null)
                {
                    throw new ExceptionShaderGenError("Given output '" + output.getName() + "' has no interface valid for shader generation");
                }

                graph = new ShaderGraph(parent, name, element.getDocument(), context);

                // Clear classification
                graph._classification = 0;

                // Create input sockets
                graph.addInputSockets(@interface, context);

                // Create the given output socket
                TypeDesc outputType = context.getTypeDesc(output.getType());
                ShaderGraphOutputSocket outputSocket = graph.addOutputSocket(output.getName(), outputType);
                outputSocket.setPath(output.getNamePath());
                string outputUnit = output.getUnit();
                if (outputUnit.Length != 0)
                {
                    outputSocket.setUnit(outputUnit);
                }
                string outputColorSpace = output.getColorSpace();
                if (outputColorSpace.Length != 0)
                {
                    outputSocket.setColorSpace(outputColorSpace);
                }

                // Start traversal from this output
                root = output;
            }

            else if (element.isA<Node>())
            {
                Node node = element.asA<Node>();
                NodeDef nodeDef = node.getNodeDef();
                if (nodeDef == null)
                {
                    throw new ExceptionShaderGenError("Could not find a nodedef for node '" + node.getName() + "'");
                }

                graph = new ShaderGraph(parent, name, element.getDocument(), context);

                // Create input sockets
                graph.addInputSockets(nodeDef, context);

                // Create output sockets
                graph.addOutputSockets(nodeDef, context);

                // Create this shader node in the graph.
                ShaderNode newNode = graph.createNode(node.getName(), node.getNamePath(), nodeDef, context);

                // Share metadata.
                graph.setMetadata(newNode.getMetadata());

                // Connect it to the graph outputs
                for (int i = 0; i < newNode.numOutputs(); ++i)
                {
                    ShaderGraphOutputSocket outputSocket = graph.getOutputSocket(i);
                    outputSocket.makeConnection(newNode.getOutput(i));
                    outputSocket.setPath(node.getNamePath());
                }

                // Handle node input ports
                foreach (Input nodedefInput in nodeDef.getActiveInputs())
                {
                    ShaderGraphInputSocket inputSocket = graph.getInputSocket(nodedefInput.getName());
                    ShaderInput input = newNode.getInput(nodedefInput.getName());
                    if (inputSocket == null || input == null)
                    {
                        throw new ExceptionShaderGenError("Node input '" + nodedefInput.getName() + "' doesn't match an existing input on graph '" + graph.getName() + "'");
                    }

                    // Copy data from node element to shadergen representation
                    Input nodeInput = node.getInput(nodedefInput.getName());
                    if (nodeInput != null)
                    {
                        Value value = nodeInput.getResolvedValue();
                        if (value == null)
                        {
                            Input interfaceInput = nodeInput.getInterfaceInput();
                            if (interfaceInput != null)
                            {
                                value= interfaceInput.getResolvedValue();
                            }
                        }
                        if (value != null)
                        {
                            string valueString = value.getValueString();
                            (TypeDesc, Value) enumResult = default;
                            TypeDesc type = context.getTypeDesc(nodedefInput.getType());
                            string enumNames = nodedefInput.getAttribute(ValueElement.ENUM_ATTRIBUTE);
                            if (context.getShaderGenerator().getSyntax().remapEnumeration(valueString, type, enumNames, ref enumResult))
                            {
                                inputSocket.setValue(enumResult.Item2);
                            }
                            else
                            {
                                inputSocket.setValue(value);
                            }
                        }

                        input.setBindInput();
                        string path = nodeInput.getNamePath();
                        if (path.Length != 0)
                        {
                            inputSocket.setPath(path);
                            input.setPath(path);
                        }
                        string unit = nodeInput.getUnit();
                        if (unit.Length != 0)
                        {
                            inputSocket.setUnit(unit);
                            input.setUnit(unit);
                        }
                        string colorSpace = nodeInput.getColorSpace();
                        if (colorSpace.Length != 0)
                        {
                            inputSocket.setColorSpace(colorSpace);
                            input.setColorSpace(colorSpace);
                        }
                    }

                    // Connect graph socket to the node input
                    inputSocket.makeConnection(input);

                    // Share metadata.
                    inputSocket.setMetadata(input.getMetadata());
                }

                // Apply color and unit transforms to each input.
                graph.applyInputTransforms(node, newNode, context);

                // Set root for upstream dependency traversal
                root = node;
            }

            if (graph == null)
            {
                throw new ExceptionShaderGenError("Shader generation from element '" + element.getName() + "' of type '" + element.getCategory() + "' is not supported");
            }

            // Traverse and create all dependencies upstream
            if (root != null && context.getOptions().addUpstreamDependencies)
            {
                graph.addUpstreamDependencies(root, context);
            }

            graph.finalize(context);

            return graph;
        }

        // Create a new shader graph from a nodegraph.
        public static ShaderGraph create(ShaderGraph parent, NodeGraph nodeGraph, GenContext context)
        {
            NodeDef nodeDef = nodeGraph.getNodeDef();
            if (nodeDef == null)
            {
                throw new ExceptionShaderGenError("Can't find nodedef '" + nodeGraph.getNodeDefString() + "' referenced by nodegraph '" + nodeGraph.getName() + "'");
            }

            string graphName = nodeGraph.getName();
            context.getShaderGenerator().getSyntax().makeValidName(ref graphName);
            ShaderGraph graph = new ShaderGraph(parent, graphName, nodeGraph.getDocument(), context);

            // Clear classification
            graph._classification = 0;

            // Create input sockets from the nodedef
            graph.addInputSockets(nodeDef, context);

            // Create output sockets from the nodegraph
            graph.addOutputSockets(nodeGraph, context);

            // Traverse all outputs and create all internal nodes
            foreach (Output graphOutput in nodeGraph.getActiveOutputs())
            {
                graph.addUpstreamDependencies(graphOutput, context);
            }

            // Finalize the graph
            graph.finalize(context);

            return graph;
        }

        // Return true if this node is a graph.
        public override bool isAGraph()
        {
            return true;
        }

        // Get a vector of all nodes in order
        public List<ShaderNode> getNodes()
        {
            return _nodeOrder;
        }

        // Get number of input sockets
        public int numInputSockets()
        {
            return numOutputs();
        }

        // Get number of output sockets
        public int numOutputSockets()
        {
            return numInputs();
        }

        // Get socket by index
        public ShaderGraphInputSocket getInputSocket(int index)
        {
            return getOutput(index);
        }

        public ShaderGraphOutputSocket getOutputSocket(int index = 0)
        {
            return getInput(index);
        }

        // Get socket by name
        public ShaderGraphInputSocket getInputSocket(string name)
        {
            return getOutput(name);
        }

        public ShaderGraphOutputSocket getOutputSocket(string name)
        {
            return getInput(name);
        }

        // Get vector of sockets
        public List<ShaderGraphInputSocket> getInputSockets()
        {
            return _outputOrder;
        }

        public List<ShaderGraphOutputSocket> getOutputSockets()
        {
            return _inputOrder;
        }

        // Apply color and unit transforms to each input of a node.
        public void applyInputTransforms(Node node, ShaderNode shaderNode, GenContext context)
        {
            ColorManagementSystem colorManagementSystem = context.getShaderGenerator().getColorManagementSystem();
            UnitSystem unitSystem = context.getShaderGenerator().getUnitSystem();

            string targetColorSpace = context.getOptions().targetColorSpaceOverride.Length == 0 ?
                                             _document.getActiveColorSpace() :
                                             context.getOptions().targetColorSpaceOverride;
            string targetDistanceUnit = context.getOptions().targetDistanceUnit;

            foreach (Input input in node.getInputs())
            {
                if (input.hasValue() || input.hasInterfaceName())
                {
                    string sourceColorSpace = input.getActiveColorSpace();
                    if (input.getType() == FILENAME_TYPE_STRING && (node.isColorType() || node.isMultiOutputType()))
                    {
                        // Adjust the source color space for filename interface names.
                        if (input.hasInterfaceName())
                        {
                            foreach (Node parentNode in context.getParentNodes())
                            {
                                if (!parentNode.isColorType() && !parentNode.isMultiOutputType())
                                {
                                    Input interfaceInput = parentNode.getInput(input.getInterfaceName());
                                    string interfaceColorSpace = interfaceInput != null ? interfaceInput.getActiveColorSpace() : EMPTY_STRING;
                                    if (interfaceColorSpace.Length != 0)
                                    {
                                        sourceColorSpace = interfaceColorSpace;
                                    }
                                }
                            }
                        }

                        foreach (ShaderOutput shaderOutput in shaderNode.getOutputs())
                        {
                            populateColorTransformMap(colorManagementSystem, shaderOutput, sourceColorSpace, targetColorSpace, false);
                            populateUnitTransformMap(unitSystem, shaderOutput, input, targetDistanceUnit, false);
                        }
                    }
                    else
                    {
                        ShaderInput shaderInput = shaderNode.getInput(input.getName());
                        populateColorTransformMap(colorManagementSystem, shaderInput, sourceColorSpace, targetColorSpace, true);
                        populateUnitTransformMap(unitSystem, shaderInput, input, targetDistanceUnit, true);
                    }
                }
            }
        }

        // Create a new node in the graph
        public ShaderNode createNode(Node node, GenContext context)
        {
            NodeDef nodeDef = node.getNodeDef();

            // Create this node in the graph.
            context.pushParentNode(node);
            ShaderNode newNode = createNode(node.getName(), node.getNamePath(), nodeDef, context);
            newNode.initialize(node, nodeDef, context);
            context.popParentNode();

            // Check if any of the node inputs should be connected to the graph interface
            foreach (ValueElement elem in node.getChildrenOfType<ValueElement>())
            {
                string interfaceName = elem.getInterfaceName();
                if (interfaceName.Length != 0)
                {
                    ShaderGraphInputSocket inputSocket = getInputSocket(interfaceName);
                    if (inputSocket != null)
                    {
                        ShaderInput input = newNode.getInput(elem.getName());

                        if (input != null)
                        {
                            input.makeConnection(inputSocket);
                        }
                    }
                }
            }

            // Handle the "defaultgeomprop" directives on the nodedef inputs.
            // Create and connect default geometric nodes on unconnected inputs.
            foreach (Input nodeDefInput in nodeDef.getActiveInputs())
            {
                ShaderInput input = newNode.getInput(nodeDefInput.getName());
                Input nodeInput = node.getInput(nodeDefInput.getName());

                string connection = nodeInput != null ? nodeInput.getNodeName() : EMPTY_STRING;
                if (connection.Length == 0 && input.getConnection() == null)
                {
                    GeomPropDef geomprop = nodeDefInput.getDefaultGeomProp();
                    if (geomprop != null)
                    {
                        addDefaultGeomNode(input, geomprop, context);
                    }
                }
            }

            // Apply color and unit transforms to each input.
            applyInputTransforms(node, newNode, context);

            return newNode;
        }

        public ShaderNode inlineNodeBeforeOutput(ShaderGraphOutputSocket output, string newNodeName, string nodeDefName, string inputName, string outputName, GenContext context)
        {
            NodeDef nodeDef = _document.getNodeDef(nodeDefName);
            if (nodeDef == null)
            {
                throw new ExceptionShaderGenError("Cannot find NodeDef '"+nodeDefName+"' when inserting node '"+newNodeName+"'");
            }

            // Check to see if the nodedef has the specified input/output ports
            Output nodeDefOutput = nodeDef.getOutput(outputName);
            if (nodeDefOutput == null)
            {
                throw new ExceptionShaderGenError("Output '"+outputName+"' not found on NodeDef '"+nodeDefName+"'");
            }

            Input nodeDefInput = null;
            if (inputName.Length != 0)
            {
                // Only look for the input if we are given an inputName. It's valid to insert the node
                // without any upstream connection, and so an input name is not required.
                nodeDefInput = nodeDef.getInput(inputName);
                if (nodeDefInput == null)
                {
                    throw new ExceptionShaderGenError("Input '"+inputName+"' not found on NodeDef '"+nodeDefName+"'");
                }
            }

            // record the previously connected upstream
            ShaderOutput originalUpstream = output.getConnection();

            if (nodeDefInput != null && originalUpstream != null)
            {
                // if we're going to attempt to connect these - we need to check the types match
                // we do this before creating any new data
                if (nodeDefInput.getType() != originalUpstream.getType().getName())
                {
                    throw new ExceptionShaderGenError("Cannot connect ports of mismatched types '"+nodeDefInput.getType()+"' and '"+originalUpstream.getType().getName()+"' when inserting node");
                }
            }

            // create the new node, and connect its output to the provided graph output
            ShaderNode newNode = createNode(newNodeName, newNodeName, nodeDef, context);
            if (newNode == null)
            {
                throw new ExceptionShaderGenError("Error while creating node '"+newNodeName+"' of type '"+nodeDefName+"'");
            }
            ShaderOutput newNodeOutput = newNode.getOutput(outputName);
            newNodeOutput.setVariable(newNodeOutput.getFullName());
            output.makeConnection(newNodeOutput);

            // update the type of the graph output port to match the new node output
            output.setType(newNodeOutput.getType());

            // if there was an original upstream node connected to graph output
            // and we found the named input port - which means we were given a inputName (it was not empty string)
            // connect this to the new node at the provided input name.
            if (originalUpstream != null && nodeDefInput != null)
            {
                // update the variable name for the input and connect the original upstream
                // we already validated the types match above.
                ShaderInput newNodeInput = newNode.getInput(inputName);
                newNodeInput.setVariable(newNodeInput.getFullName());

                originalUpstream.makeConnection(newNodeInput);
            }

            return newNode;
        }

        // Add input sockets
        public ShaderGraphInputSocket addInputSocket(string name, TypeDesc type)
        {
            return base.addOutput(name, type);
        }

        // Add output sockets
        public ShaderGraphOutputSocket addOutputSocket(string name, TypeDesc type)
        {
            return base.addInput(name, type);
        }

        // Add a default geometric node and connect to the given input.
        public void addDefaultGeomNode(ShaderInput input, GeomPropDef geomprop, GenContext context)
        {
            string geomNodeName = "geomprop_" + geomprop.getName();
            ShaderNode node = getNode(geomNodeName);

            if (node == null)
            {
                // Find the nodedef for the geometric node referenced by the geomprop. Use the type of the
                // input here and ignore the type of the geomprop. They are required to have the same type.
                string geomNodeDefName = "ND_" + geomprop.getGeomProp() + "_" + input.getType().getName();
                NodeDef geomNodeDef = _document.getNodeDef(geomNodeDefName);
                if (geomNodeDef == null)
                {
                    throw new ExceptionShaderGenError("Could not find a nodedef named '" + geomNodeDefName +
                                                  "' for defaultgeomprop on input '" + input.getFullName() + "'");
                }

                ShaderNode geomNode = ShaderNode.create(this, geomNodeName, geomNodeDef, context);
                addNode(geomNode);

                // Set node inputs if given.
                string namePath = geomprop.getNamePath();
                string space = geomprop.getSpace();
                if (space.Length != 0)
                {
                    ShaderInput spaceInput = geomNode.getInput(GeomPropDef.SPACE_ATTRIBUTE);
                    ValueElement nodeDefSpaceInput = geomNodeDef.getActiveValueElement(GeomPropDef.SPACE_ATTRIBUTE);
                    if (spaceInput != null && nodeDefSpaceInput != null)
                    {
                        (TypeDesc, Value) enumResult = default;
                        string enumNames = nodeDefSpaceInput.getAttribute(ValueElement.ENUM_ATTRIBUTE);
                        TypeDesc portType = context.getTypeDesc(nodeDefSpaceInput.getType());
                        if (context.getShaderGenerator().getSyntax().remapEnumeration(space, portType, enumNames, ref enumResult))
                        {
                            spaceInput.setValue(enumResult.Item2);
                        }
                        else
                        {
                            spaceInput.setValue(Value.createValue<string>(space));
                        }
                        spaceInput.setPath(namePath);
                    }
                }
                string index = geomprop.getIndex();
                if (index.Length != 0)
                {
                    ShaderInput indexInput = geomNode.getInput("index");
                    if (indexInput != null)
                    {
                        indexInput.setValue(Value.createValue<string>(index));
                        indexInput.setPath(namePath);
                    }
                }
                string geomProp = geomprop.getGeomProp();
                if (geomProp.Length != 0)
                {
                    ShaderInput geomPropInput = geomNode.getInput(GeomPropDef.GEOM_PROP_ATTRIBUTE);
                    if (geomPropInput != null)
                    {
                        geomPropInput.setValue(Value.createValue<string>(geomProp));
                        geomPropInput.setPath(namePath);
                    }
                }

                node = geomNode;

                // Assign a unique variable name for the node output.
                Syntax syntax = context.getShaderGenerator().getSyntax();
                ShaderOutput output = node.getOutput();
                string variable = output.getFullName();
                variable = syntax.getVariableName(variable, output.getType(), _identifiers);
                output.setVariable(variable);
            }

            input.makeConnection(node.getOutput());
        }

        // Sort the nodes in topological order.
        public void topologicalSort()
        {
            // Calculate a topological order of the children, using Kahn's algorithm
            // to avoid recursion.
            //
            // Running time: O(numNodes + numEdges).

            // Calculate in-degrees for all nodes, and enqueue those with degree 0.
            Dictionary<ShaderNode, int> inDegree = new Dictionary<ShaderNode, int>(_nodeMap.Count, ReferenceEqualityComparer.Instance);
            Queue<ShaderNode> nodeQueue = new Queue<ShaderNode>();
            foreach (ShaderNode node in _nodeOrder)
            {
                int connectionCount = 0;
                foreach (ShaderInput input in node.getInputs())
                {
                    if (input.getConnection() != null && input.getConnection().getNode() != this)
                    {
                        ++connectionCount;
                    }
                }

                inDegree[node] = connectionCount;

                if (connectionCount == 0)
                {
                    nodeQueue.Enqueue(node);
                }
            }

            while (_nodeOrder.Count > _nodeMap.Count)
            {
                _nodeOrder.RemoveAt(_nodeOrder.Count - 1);
            }
            while (_nodeOrder.Count < _nodeMap.Count)
            {
                _nodeOrder.Add(null);
            }
            int count = 0;

            while (nodeQueue.Count != 0)
            {
                // Pop the queue and add to topological order.
                ShaderNode node = nodeQueue.Dequeue();
                _nodeOrder[count++] = node;

                // Find connected nodes and decrease their in-degree,
                // adding node to the queue if in-degrees becomes 0.
                foreach (ShaderOutput output in node.getOutputs())
                {
                    foreach (ShaderInput input in output.getConnections())
                    {
                        ShaderNode downstreamNode = (ShaderNode)(input.getNode());
                        if (downstreamNode != this)
                        {
                            int degree;
                            inDegree.TryGetValue(downstreamNode, out degree);
                            inDegree[downstreamNode] = --degree;
                            if (degree <= 0)
                            {
                                nodeQueue.Enqueue(downstreamNode);
                            }
                        }
                    }
                }
            }
        }

        // Return an iterator for traversal upstream from the given output
        public static ShaderGraphEdgeIterator traverseUpstream(ShaderOutput output)
        {
            return new ShaderGraphEdgeIterator(output);
        }

        // Return the map of unique identifiers used in the scope of this graph.
        public Dictionary<string, int> getIdentifierMap()
        {
            return _identifiers;
        }

        // Return the document associated with this graph.
        public Document getDocument()
        {
            return _document;
        }

        // Create a new node in the graph from a node definition.
        // @throws ExceptionShaderGenError if the graph already contains a node with the given unique identifier.
        public ShaderNode createNode(string name, string uniqueId, NodeDef nodeDef, GenContext context)
        {

            if (nodeDef == null)
            {
                throw new ExceptionShaderGenError("Could not find a nodedef for node '" + name + "'");
            }

            // Create this node in the graph.
            ShaderNode newNode = ShaderNode.create(this, name, nodeDef, context);
            newNode._uniqueId = uniqueId;
            addNode(newNode);

            return newNode;
        }

        // Bypass a node for a particular input and output,
        // effectively connecting the input's upstream connection
        // with the output's downstream connections.
        public void bypass(ShaderNode node, int inputIndex, int outputIndex = 0)
        {
            if (inputIndex >= node.numInputs())
            {
                throw new ExceptionShaderGenError("Input index '" + Mx.to_string(inputIndex) + "' out of bounds for node '" + node.getName() + "'");
            }
            if (outputIndex >= node.numOutputs())
            {
                throw new ExceptionShaderGenError("Output index '" + Mx.to_string(outputIndex) + "' out of bounds for node '" + node.getName() + "'");
            }

            ShaderInput input = node.getInput(inputIndex);
            ShaderOutput output = node.getOutput(outputIndex);

            ShaderOutput upstream = input.getConnection();
            if (upstream != null)
            {
                // Re-route the upstream output to the downstream inputs.
                // Iterate a copy of the connection vector since the
                // original vector will change when breaking connections.
                List<ShaderInput> downstreamConnections = new List<ShaderInput>(output.getConnections());
                foreach (ShaderInput downstream in downstreamConnections)
                {
                    output.breakConnection(downstream);
                    downstream.makeConnection(upstream);
                }
            }
            else
            {
                // No node connected upstream to re-route,
                // so push the input's value and element path downstream instead.
                // Iterate a copy of the connection vector since the
                // original vector will change when breaking connections.
                List<ShaderInput> downstreamConnections = new List<ShaderInput>(output.getConnections());
                foreach (ShaderInput downstream in downstreamConnections)
                {
                    output.breakConnection(downstream);
                    downstream.setValue(input.getValue());
                    downstream.setPath(input.getPath());
                    string inputUnit = input.getUnit();
                    if (inputUnit.Length != 0)
                    {
                        downstream.setUnit(inputUnit);
                    }
                    string inputColorSpace = input.getColorSpace();
                    if (inputColorSpace.Length != 0)
                    {
                        downstream.setColorSpace(inputColorSpace);
                    }
                }
            }
        }

        // Remove nodes that are no longer connected to any output.
        public void removeUnusedNodes()
        {
            HashSet<ShaderNode> usedNodesSet = new HashSet<ShaderNode>(ReferenceEqualityComparer.Instance);
            List<ShaderNode> usedNodesVec = new List<ShaderNode>();

            // Traverse the graph to find nodes still in use.
            foreach (ShaderGraphOutputSocket outputSocket in getOutputSockets())
            {
                // Make sure to not include connections to the graph itself.
                ShaderOutput upstreamPort = outputSocket.getConnection();
                if (upstreamPort != null && upstreamPort.getNode() != this)
                {
                    foreach (ShaderGraphEdge edge in traverseUpstream(upstreamPort))
                    {
                        ShaderNode node = edge.upstream.getNode();
                        if (!usedNodesSet.Contains(node))
                        {
                            usedNodesSet.Add(node);
                            usedNodesVec.Add(node);
                        }
                    }
                }
            }

            // Remove any unused nodes.
            foreach (KeyValuePair<string, ShaderNode> it in new List<KeyValuePair<string, ShaderNode>>(_nodeMap))
            {
                if (!usedNodesSet.Contains(it.Value))
                {
                    // Break all connections.
                    disconnect(it.Value);

                    // Erase from storage.
                    _nodeMap.Remove(it.Key);
                }
            }

            _nodeOrder = usedNodesVec;
        }

        // Rewire all downstream connections from one output to another.
        public void replaceOutput(ShaderOutput oldOutput, ShaderOutput newOutput)
        {
            List<ShaderInput> downstreamConnections = new List<ShaderInput>(oldOutput.getConnections());
            foreach (ShaderInput downstream in downstreamConnections)
            {
                oldOutput.breakConnection(downstream);
                downstream.makeConnection(newOutput);
            }
        }

        // ShaderGraph Protected Methods
        // Create node connections corresponding to the connection between a pair of elements.
        // @param downstreamElement Element representing the node to connect to.
        // @param upstreamElement Element representing the node to connect from
        // @param connectingElement If non-null, specifies the element on on the downstream node to connect to.
        // @param context Context for generation.
        protected void createConnectedNodes(Element downstreamElement, Element upstreamElement, Element connectingElement, GenContext context)
        {

            // Create the node if it doesn't exist.
            Node upstreamNode = upstreamElement.asA<Node>();
            if (upstreamNode == null)
            {
                throw new ExceptionShaderGenError("Upstream element to connect is not a node '" +
                                              upstreamElement.getName() + "'");
            }
            ShaderNode newNode = getNode(upstreamNode.getNamePath());
            if (newNode == null)
            {
                newNode = createNode(upstreamNode, context);
            }

            // Handle interface inputs with default geometric properties.
            foreach (Input activeInput in upstreamNode.getActiveInputs())
            {
                if (!activeInput.hasInterfaceName() || activeInput.getConnectedNode() != null)
                {
                    continue;
                }

                Input graphInput = activeInput.getInterfaceInput();
                if (graphInput != null && graphInput.hasDefaultGeomPropString())
                {
                    ShaderInput shaderInput = newNode.getInput(activeInput.getName());
                    addDefaultGeomNode(shaderInput, graphInput.getDefaultGeomProp(), context);
                }
            }

            //
            // Make connections
            //

            // Find the output to connect to.
            if (connectingElement == null && downstreamElement.isA<Output>())
            {
                // Edge case for having an output downstream but no connecting
                // element (input) reported upstream. In this case we set the
                // output itself as connecting element, which handles finding
                // the nodedef output in case of a multioutput node upstream.
                connectingElement = downstreamElement.asA<Output>();
            }
            Output nodeDefOutput = connectingElement != null ? upstreamNode.getNodeDefOutput(connectingElement) : null;
            ShaderOutput output = nodeDefOutput != null ? newNode.getOutput(nodeDefOutput.getName()) : newNode.getOutput();
            if (output == null)
            {
                throw new ExceptionShaderGenError("Could not find an output named '" + (nodeDefOutput != null ? nodeDefOutput.getName() : "out") +
                                              "' on upstream node '" + upstreamNode.getName() + "'");
            }

            // Connect to downstream node or graph output socket.
            Node downstreamNode = downstreamElement.asA<Node>();
            if (downstreamNode != null)
            {
                ShaderNode downstream = getNode(downstreamNode.getNamePath());
                if (downstream != null)
                {
                    if (downstream == newNode)
                    {
                        throw new ExceptionShaderGenError("Upstream node '" + downstream.getName() + "' has itself as downstream node, creating a loop");
                    }

                    if (connectingElement != null)
                    {
                        ShaderInput input = downstream.getInput(connectingElement.getName());
                        if (input == null)
                        {
                            throw new ExceptionShaderGenError("Could not find an input named '" + connectingElement.getName() +
                                                          "' on downstream node '" + downstream.getName() + "'");
                        }
                        input.makeConnection(output);
                    }
                    else
                    {
                        throw new ExceptionShaderGenError("No connecting element for downstream node '" + downstreamNode.getName() + "'");
                    }
                }
            }
            else
            {
                // Not a node, then it must be an output
                ShaderGraphOutputSocket outputSocket = getOutputSocket(downstreamElement.getName());
                if (outputSocket != null)
                {
                    outputSocket.makeConnection(output);
                }
            }
        }

        // Add a node to the graph, keyed by the node's unique identifier.
        protected void addNode(ShaderNode node)
        {
            // Replacing an existing node would leave dangling pointers in the node order and connections.
            if (!_nodeMap.TryAdd(node.getUniqueId(), node))
            {
                throw new ExceptionShaderGenError("Shader graph already contains a node with unique ID '" + node.getUniqueId() + "'.");
            }
            _nodeOrder.Add(node);
        }

        // Add input sockets from an interface element (nodedef, nodegraph or node)
        protected void addInputSockets(InterfaceElement elem, GenContext context)
        {
            foreach (Input input in elem.getActiveInputs())
            {
                ShaderGraphInputSocket inputSocket = null;
                Value portValue = input.getResolvedValue();
                string portValueString = portValue != null ? portValue.getValueString() : EMPTY_STRING;
                (TypeDesc, Value) enumResult = default;
                string enumNames = input.getAttribute(ValueElement.ENUM_ATTRIBUTE);
                TypeDesc portType = context.getTypeDesc(input.getType());
                if (context.getShaderGenerator().getSyntax().remapEnumeration(portValueString, portType, enumNames, ref enumResult))
                {
                    inputSocket = addInputSocket(input.getName(), enumResult.Item1);
                    inputSocket.setValue(enumResult.Item2);
                }
                else
                {
                    inputSocket = addInputSocket(input.getName(), portType);
                    if (portValueString.Length != 0)
                    {
                        inputSocket.setValue(portValue);
                    }
                }
                if (input.getIsUniform())
                {
                    inputSocket.setUniform();
                }
                inputSocket.setPath(input.getNamePath());
                GeomPropDef geomprop = input.getDefaultGeomProp();
                if (geomprop != null)
                {
                    inputSocket.setGeomProp(geomprop.getName());
                }
            }
        }

        // Add output sockets from an interface element (nodedef, nodegraph or node)
        protected void addOutputSockets(InterfaceElement elem, GenContext context)
        {
            foreach (Output output in elem.getActiveOutputs())
            {
                addOutputSocket(output.getName(), context.getTypeDesc(output.getType()));
            }
            if (numOutputSockets() == 0)
            {
                addOutputSocket("out", context.getTypeDesc(elem.getType()));
            }
        }

        // Traverse from the given root element and add all dependencies upstream.
        // The traversal is done in the context of a material, if given, to include
        // bind input elements in the traversal.
        protected void addUpstreamDependencies(Element root, GenContext context)
        {

            HashSet<Element> processedOutputs = new HashSet<Element>(ReferenceEqualityComparer.Instance);

            foreach (Edge edge in root.traverseGraph())
            {
                Element upstreamElement = edge.getUpstreamElement();
                if (upstreamElement == null)
                {
                    continue;
                }

                Element downstreamElement = edge.getDownstreamElement();

                // Early out if downstream element is an output that
                // we have already processed. This might happen since
                // we perform jumps over output elements below.
                if (processedOutputs.Contains(downstreamElement))
                {
                    continue;
                }

                // If upstream is an output jump to the actual node connected to the output.
                if (upstreamElement.isA<Output>())
                {
                    // Record this output so we don't process it again when it
                    // shows up as a downstream element in the next iteration.
                    processedOutputs.Add(upstreamElement);

                    upstreamElement = upstreamElement.asA<Output>().getConnectedNode();
                    if (upstreamElement == null)
                    {
                        continue;
                    }
                }

                createConnectedNodes(downstreamElement,
                                     upstreamElement,
                                     edge.getConnectingElement(),
                                     context);
            }
        }

        // Add a color transform node and connect to the given input.
        protected void addColorTransformNode(ShaderInput input, ColorSpaceTransform transform, GenContext context)
        {
            if (input.getConnection() != null && !input.isBindInput())
            {
                // Ignore connected inputs, which are unaffected by color space bindings.
                return;
            }

            ColorManagementSystem colorManagementSystem = context.getShaderGenerator().getColorManagementSystem();
            if (colorManagementSystem == null)
            {
                return;
            }
            string colorTransformNodeName = input.getFullName() + "_cm";
            ShaderNode colorTransformNodePtr = colorManagementSystem.createNode(this, transform, colorTransformNodeName, context);

            if (colorTransformNodePtr != null)
            {
                addNode(colorTransformNodePtr);

                ShaderNode colorTransformNode = colorTransformNodePtr;
                ShaderOutput colorTransformNodeOutput = colorTransformNode.getOutput(0);

                ShaderInput shaderInput = colorTransformNode.getInput(0);
                shaderInput.setVariable(input.getFullName());
                shaderInput.setValue(input.getValue());
                shaderInput.setPath(input.getPath());
                shaderInput.setUnit(EMPTY_STRING);
                if (input.isUniform())
                {
                    // Preserve the uniform flag, so that targets which distinguish uniform
                    // and varying values (e.g. MDL) declare the published value as uniform.
                    shaderInput.setUniform();
                }

                if (input.isBindInput())
                {
                    ShaderOutput oldConnection = input.getConnection();
                    shaderInput.makeConnection(oldConnection);
                }

                input.makeConnection(colorTransformNodeOutput);
            }
        }

        // Add a color transform node and connect to the given output.
        protected void addColorTransformNode(ShaderOutput output, ColorSpaceTransform transform, GenContext context)
        {
            ColorManagementSystem colorManagementSystem = context.getShaderGenerator().getColorManagementSystem();
            if (colorManagementSystem == null)
            {
                return;
            }

            string colorTransformNodeName = output.getFullName() + "_cm";
            ShaderNode colorTransformNodePtr = colorManagementSystem.createNode(this, transform, colorTransformNodeName, context);

            if (colorTransformNodePtr != null)
            {
                addNode(colorTransformNodePtr);

                ShaderNode colorTransformNode = colorTransformNodePtr;
                ShaderOutput colorTransformNodeOutput = colorTransformNode.getOutput(0);

                List<ShaderInput> inputs = new List<ShaderInput>(output.getConnections());
                foreach (ShaderInput input in inputs)
                {
                    input.breakConnection();
                    input.makeConnection(colorTransformNodeOutput);
                }

                // Connect the node to the upstream output
                ShaderInput colorTransformNodeInput = colorTransformNode.getInput(0);
                colorTransformNodeInput.makeConnection(output);
            }
        }

        // Add a unit transform node and connect to the given input.
        protected void addUnitTransformNode(ShaderInput input, UnitTransform transform, GenContext context)
        {
            if (input.getConnection() != null && !input.isBindInput())
            {
                // Ignore connected inputs, which are unaffected by unit bindings.
                return;
            }

            UnitSystem unitSystem = context.getShaderGenerator().getUnitSystem();
            if (unitSystem == null)
            {
                return;
            }
            string unitTransformNodeName = input.getFullName() + "_unit";
            ShaderNode unitTransformNodePtr = unitSystem.createNode(this, transform, unitTransformNodeName, context);

            if (unitTransformNodePtr != null)
            {
                addNode(unitTransformNodePtr);

                ShaderNode unitTransformNode = unitTransformNodePtr;
                ShaderOutput unitTransformNodeOutput = unitTransformNode.getOutput(0);

                ShaderInput shaderInput = unitTransformNode.getInput(0);
                shaderInput.setVariable(input.getFullName());
                shaderInput.setValue(input.getValue());
                shaderInput.setPath(input.getPath());
                shaderInput.setUnit(input.getUnit());
                shaderInput.setColorSpace(input.getColorSpace());
                if (input.isUniform())
                {
                    shaderInput.setUniform();
                }

                if (input.isBindInput())
                {
                    ShaderOutput oldConnection = input.getConnection();
                    shaderInput.makeConnection(oldConnection);
                }

                input.makeConnection(unitTransformNodeOutput);
            }
        }

        // Add a unit transform node and connect to the given output.
        protected void addUnitTransformNode(ShaderOutput output, UnitTransform transform, GenContext context)
        {
            UnitSystem unitSystem = context.getShaderGenerator().getUnitSystem();
            if (unitSystem == null)
            {
                return;
            }

            string unitTransformNodeName = output.getFullName() + "_unit";
            ShaderNode unitTransformNodePtr = unitSystem.createNode(this, transform, unitTransformNodeName, context);

            if (unitTransformNodePtr != null)
            {
                addNode(unitTransformNodePtr);

                ShaderNode unitTransformNode = unitTransformNodePtr;
                ShaderOutput unitTransformNodeOutput = unitTransformNode.getOutput(0);

                List<ShaderInput> inputs = new List<ShaderInput>(output.getConnections());
                foreach (ShaderInput input in inputs)
                {
                    input.breakConnection();
                    input.makeConnection(unitTransformNodeOutput);
                }

                // Connect the node to the upstream output
                ShaderInput unitTransformNodeInput = unitTransformNode.getInput(0);
                unitTransformNodeInput.makeConnection(output);
            }
        }

        // Perform all post-build operations on the graph.
        protected void finalize(GenContext context)
        {
            // Allow node implementations to update the classification
            // on its node instances
            foreach (ShaderNode node in getNodes())
            {
                node.getImplementation().addClassification(node);
            }

            // Add classification according to root node
            ShaderGraphOutputSocket outputSocket = getOutputSocket();
            if (outputSocket.getConnection() != null)
            {
                addClassification(outputSocket.getConnection().getNode().getClassification());
            }

            // Insert color transformation nodes where needed
            if (context.getOptions().emitColorTransforms)
            {
                foreach ((ShaderInput, ColorSpaceTransform) it in _inputColorTransformMap)
                {
                    addColorTransformNode(it.Item1, it.Item2, context);
                }
                foreach ((ShaderOutput, ColorSpaceTransform) it in _outputColorTransformMap)
                {
                    addColorTransformNode(it.Item1, it.Item2, context);
                }
            }
            _inputColorTransformMap.Clear();
            _outputColorTransformMap.Clear();

            // Insert unit transformation nodes where needed
            foreach ((ShaderInput, UnitTransform) it in _inputUnitTransformMap)
            {
                addUnitTransformNode(it.Item1, it.Item2, context);
            }
            foreach ((ShaderOutput, UnitTransform) it in _outputUnitTransformMap)
            {
                addUnitTransformNode(it.Item1, it.Item2, context);
            }
            _inputUnitTransformMap.Clear();
            _outputUnitTransformMap.Clear();

            // Run registered graph refactoring passes.
            int totalEdits = 0;
            foreach (ShaderGraphRefactor refactor in context.getShaderGenerator().getRefactors())
            {
                totalEdits += refactor.execute(this, context);
            }

            // Remove unused nodes if any refactoring pass made edits.
            if (totalEdits > 0)
            {
                removeUnusedNodes();
            }

            // Sort the nodes in topological order.
            topologicalSort();

            if (context.getOptions().shaderInterfaceType == ShaderInterfaceType.SHADER_INTERFACE_COMPLETE)
            {
                // Publish all node inputs that has not been connected already.
                foreach (ShaderNode node in getNodes())
                {
                    foreach (ShaderInput input in node.getInputs())
                    {
                        if (input.getConnection() == null)
                        {
                            // Check if the type is editable otherwise we can't
                            // publish the input as an editable uniform.
                            if (!input.getType().isClosure() && node.isEditable(input))
                            {
                                // Use a consistent naming convention: < nodename>_<inputname>
                                // so application side can figure out what uniforms to set
                                // when node inputs change on application side.
                                string interfaceName = node.getName() + "_" + input.getName();

                                ShaderGraphInputSocket inputSocket = getInputSocket(interfaceName);
                                if (inputSocket == null)
                                {
                                    inputSocket = addInputSocket(interfaceName, input.getType());
                                    inputSocket.setPath(input.getPath());
                                    inputSocket.setValue(input.getValue());
                                    inputSocket.setUnit(input.getUnit());
                                    inputSocket.setColorSpace(input.getColorSpace());
                                    if (input.isUniform())
                                    {
                                        inputSocket.setUniform();
                                    }
                                }
                                inputSocket.makeConnection(input);
                                inputSocket.setMetadata(input.getMetadata());
                            }
                        }
                    }
                }
            }

            // Set variable names for inputs and outputs in the graph.
            setVariableNames(context);
        }

        // For inputs and outputs in the graph set the variable names to be used
        // in generated code. Making sure variable names are valid and unique
        // to avoid name conflicts during shader generation.
        protected void setVariableNames(GenContext context)
        {
            // Make sure inputs and outputs have variable names valid for the
            // target shading language, and are unique to avoid name conflicts.

            Syntax syntax = context.getShaderGenerator().getSyntax();

            foreach (ShaderGraphInputSocket inputSocket in getInputSockets())
            {
                string variable = syntax.getVariableName(inputSocket.getName(), inputSocket.getType(), _identifiers);
                inputSocket.setVariable(variable);
            }
            foreach (ShaderGraphOutputSocket outputSocket in getOutputSockets())
            {
                string variable = syntax.getVariableName(outputSocket.getName(), outputSocket.getType(), _identifiers);
                outputSocket.setVariable(variable);
            }
            foreach (ShaderNode node in getNodes())
            {
                foreach (ShaderInput input in node.getInputs())
                {
                    string variable = input.getFullName();
                    variable = syntax.getVariableName(variable, input.getType(), _identifiers);
                    input.setVariable(variable);
                }
                foreach (ShaderOutput output in node.getOutputs())
                {
                    string variable = output.getFullName();
                    variable = syntax.getVariableName(variable, output.getType(), _identifiers);
                    output.setVariable(variable);
                }
            }
        }

        // Populate the color transform map for the given shader port, if the provided combination of
        // source and target color spaces are supported for its data type.
        protected void populateColorTransformMap(ColorManagementSystem colorManagementSystem, ShaderPort shaderPort, string sourceColorSpace, string targetColorSpace, bool asInput)
        {
            if (shaderPort == null || sourceColorSpace.Length == 0 || targetColorSpace.Length == 0)
            {
                return;
            }

            // A transform that the color management system considers a no-op, such as one between
            // a legacy color space name and its color interop equivalent, or one involving a no-op
            // color space such as "data", is omitted from the graph and leaves the port's color
            // space unset. Without a color management system, only identical names and the
            // spec-reserved no-op color spaces are recognized.
            bool isNoOpTransform = colorManagementSystem != null ?
                                         colorManagementSystem.isNoOpTransform(sourceColorSpace, targetColorSpace) :
                                         sourceColorSpace == targetColorSpace ||
                                         ColorManagementSystem.isReservedNoOpColorSpace(sourceColorSpace) ||
                                         ColorManagementSystem.isReservedNoOpColorSpace(targetColorSpace);
            if (isNoOpTransform)
            {
                return;
            }

            if (shaderPort.getType() == Type.COLOR3 || shaderPort.getType() == Type.COLOR4)
            {
                // Store the source color space on the shader port.
                shaderPort.setColorSpace(sourceColorSpace);

                // Update the color transform map, if a color management system is provided.
                if (colorManagementSystem != null)
                {
                    ColorSpaceTransform transform = new ColorSpaceTransform(sourceColorSpace, targetColorSpace, shaderPort.getType());
                    if (colorManagementSystem.supportsTransform(transform))
                    {
                        if (asInput)
                        {
                            _inputColorTransformMap.Add(((ShaderInput)(shaderPort), transform));
                        }
                        else
                        {
                            (ShaderOutput, ColorSpaceTransform) entry = ((ShaderOutput)(shaderPort), transform);
                            // An output can carry only one transform, even when filename inputs use different color spaces.
                            // Retain the first request until per-image transforms inside compound nodes are supported.
                            bool requested = false;
                            foreach ((ShaderOutput, ColorSpaceTransform) request in _outputColorTransformMap)
                            {
                                if (request.Item1 == shaderPort)
                                {
                                    requested = true;
                                    break;
                                }
                            }
                            if (!requested)
                            {
                                _outputColorTransformMap.Add(entry);
                            }
                        }
                    }
                    else
                    {
                        throw new ExceptionShaderGenError("Unsupported color space transform from '" +
                                                      sourceColorSpace + "' to '" + targetColorSpace + "'.");
                    }
                }
            }
        }

        // Populates the appropriate unit transform map if the provided input/parameter or output
        // has a unit attribute and is of the supported type
        protected void populateUnitTransformMap(UnitSystem unitSystem, ShaderPort shaderPort, ValueElement input, string globalTargetUnitSpace, bool asInput)
        {
            if (unitSystem == null || globalTargetUnitSpace.Length == 0)
            {
                return;
            }

            string sourceUnitSpace = input.getUnit();
            if (shaderPort == null || sourceUnitSpace.Length == 0)
            {
                return;
            }

            string unitType = input.getUnitType();
            if (input.getDocument().getUnitTypeDef(unitType) == null)
            {
                return;
            }

            string targetUnitSpace = input.getActiveUnit();
            if (targetUnitSpace.Length == 0)
            {
                targetUnitSpace = globalTargetUnitSpace;
            }

            // TODO: Consider this to be an optimization option as
            // this allows for the source and target unit to be the same value
            // while still allowing target unit updates on a compiled shader as the
            // target is exposed as an input uniform.
            //if (sourceUnitSpace == targetUnitSpace)
            //{
            //    return;
            //}

            // Only support conversion for float and vectors. arrays, matrices are not supported.
            // TODO: This should be provided by the UnitSystem.
            bool supportedType = (shaderPort.getType() == Type.FLOAT ||
                                  shaderPort.getType() == Type.VECTOR2 ||
                                  shaderPort.getType() == Type.VECTOR3 ||
                                  shaderPort.getType() == Type.VECTOR4);
            if (supportedType)
            {
                UnitTransform transform = new UnitTransform(sourceUnitSpace, targetUnitSpace, shaderPort.getType(), unitType);
                if (unitSystem.supportsTransform(transform))
                {
                    shaderPort.setUnit(sourceUnitSpace);
                    if (asInput)
                    {
                        _inputUnitTransformMap.Add(((ShaderInput)(shaderPort), transform));
                    }
                    else
                    {
                        _outputUnitTransformMap.Add(((ShaderOutput)(shaderPort), transform));
                    }
                }
            }
        }

        // Break all connections on a node
        protected void disconnect(ShaderNode node)
        {
            foreach (ShaderInput input in node.getInputs())
            {
                input.breakConnection();
            }
            foreach (ShaderOutput output in node.getOutputs())
            {
                output.breakConnections();
            }
        }

        // Get an internal node by its unique identifier.
        public ShaderNode getNode(string uniqueId)
        {
            ShaderNode found;
            return _nodeMap.TryGetValue(uniqueId, out found) ? found : null;
        }

        // ShaderGraph Protected Members
        protected Document _document;

        protected Dictionary<string, ShaderNode> _nodeMap = new Dictionary<string, ShaderNode>();

        protected List<ShaderNode> _nodeOrder = new List<ShaderNode>();

        protected Dictionary<string, int> _identifiers = new Dictionary<string, int>();

        // Temporary storage for inputs that require color transformations
        protected List<(ShaderInput, ColorSpaceTransform)> _inputColorTransformMap = new List<(ShaderInput, ColorSpaceTransform)>();

        // Temporary storage for inputs that require unit transformations
        protected List<(ShaderInput, UnitTransform)> _inputUnitTransformMap = new List<(ShaderInput, UnitTransform)>();

        // Temporary storage for outputs that require color transformations
        protected List<(ShaderOutput, ColorSpaceTransform)> _outputColorTransformMap = new List<(ShaderOutput, ColorSpaceTransform)>();

        // Temporary storage for outputs that require unit transformations
        protected List<(ShaderOutput, UnitTransform)> _outputUnitTransformMap = new List<(ShaderOutput, UnitTransform)>();
    }

    // ShaderGraphEdge Definition
    // An edge returned during shader graph traversal.
    public struct ShaderGraphEdge : IEquatable<ShaderGraphEdge>
    {
        // ShaderGraphEdge Public Methods
        public ShaderGraphEdge(ShaderOutput up, ShaderInput down)
        {
            upstream = up;
            downstream = down;
        }

        public static bool operator ==(ShaderGraphEdge lhs, ShaderGraphEdge rhs)
        {
            return ReferenceEquals(lhs.upstream, rhs.upstream) && ReferenceEquals(lhs.downstream, rhs.downstream);
        }

        public static bool operator !=(ShaderGraphEdge lhs, ShaderGraphEdge rhs)
        {
            return !(lhs == rhs);
        }

        public bool Equals(ShaderGraphEdge other)
        {
            return this == other;
        }

        public override bool Equals(object obj)
        {
            return obj is ShaderGraphEdge other && this == other;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(upstream),
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(downstream));
        }

        // ShaderGraphEdge Public Members
        public ShaderOutput upstream;
        public ShaderInput downstream;
    }

    // ShaderGraphEdgeIterator Definition
    // Iterator class for traversing edges between nodes in a shader graph.
    // operator++ is next(), comparison with end() is isEnd(), and the iterator
    // can be used directly in a foreach statement.
    public class ShaderGraphEdgeIterator : IEnumerable<ShaderGraphEdge>
    {
        // ShaderGraphEdgeIterator Public Methods
        public ShaderGraphEdgeIterator(ShaderOutput output)
        {
            _upstream = output;
            _downstream = null;
        }

        public bool isEnd()
        {
            return _upstream == null && _downstream == null && _stack.Count == 0;
        }

        // Dereference this iterator, returning the current output in the traversal.
        public ShaderGraphEdge getEdge()
        {
            return new ShaderGraphEdge(_upstream, _downstream);
        }

        // Iterate to the next edge in the traversal.
        // @throws ExceptionFoundCycle if a cycle is encountered.
        public ShaderGraphEdgeIterator next()
        {
            if (_upstream != null && _upstream.getNode().numInputs() != 0)
            {
                // Traverse to the first upstream edge of this element.
                _stack.Add(new StackFrame(_upstream, 0));

                ShaderInput input = _upstream.getNode().getInput(0);
                ShaderOutput output = input.getConnection();

                if (output != null && !output.getNode().isAGraph() && !skipOrMarkAsVisited(new ShaderGraphEdge(output, input)))
                {
                    extendPathUpstream(output, input);
                    return this;
                }
            }

            while (true)
            {
                if (_upstream != null)
                {
                    returnPathDownstream(_upstream);
                }

                if (_stack.Count == 0)
                {
                    // Traversal is complete.
                    _upstream = null;
                    _downstream = null;
                    _path.Clear();
                    _visitedEdges.Clear();
                    return this;
                }

                // Traverse to our siblings.
                StackFrame parentFrame = _stack[_stack.Count - 1];
                while (parentFrame.second + 1 < parentFrame.first.getNode().numInputs())
                {
                    ShaderInput input = parentFrame.first.getNode().getInput(++parentFrame.second);
                    ShaderOutput output = input.getConnection();

                    if (output != null && !output.getNode().isAGraph() && !skipOrMarkAsVisited(new ShaderGraphEdge(output, input)))
                    {
                        extendPathUpstream(output, input);
                        return this;
                    }
                }

                // Traverse to our parent's siblings.
                returnPathDownstream(parentFrame.first);
                _stack.RemoveAt(_stack.Count - 1);
            }
        }

        // Return a reference to this iterator to begin traversal
        public ShaderGraphEdgeIterator begin()
        {
            return this;
        }

        public IEnumerator<ShaderGraphEdge> GetEnumerator()
        {
            for (ShaderGraphEdgeIterator it = begin(); !it.isEnd(); it.next())
            {
                yield return it.getEdge();
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        // ShaderGraphEdgeIterator Private Methods
        private void extendPathUpstream(ShaderOutput upstream, ShaderInput downstream)
        {
            // Check for cycles.
            if (_path.Contains(upstream))
            {
                throw new ExceptionFoundCycle("Encountered cycle at element: " + upstream.getFullName());
            }

            // Extend the current path to the new element.
            _path.Add(upstream);
            _upstream = upstream;
            _downstream = downstream;
        }

        private void returnPathDownstream(ShaderOutput upstream)
        {
            _path.Remove(upstream);
            _upstream = null;
            _downstream = null;
        }

        private bool skipOrMarkAsVisited(ShaderGraphEdge edge)
        {
            bool inserted = _visitedEdges.Add(edge);
            return !inserted;
        }

        // ShaderGraphEdgeIterator Private Types
        private class StackFrame
        {
            public StackFrame(ShaderOutput first, int second)
            {
                this.first = first;
                this.second = second;
            }

            public ShaderOutput first;
            public int second;
        }

        // ShaderGraphEdgeIterator Private Members
        private ShaderOutput _upstream;
        private ShaderInput _downstream;
        private List<StackFrame> _stack = new List<StackFrame>();
        private HashSet<ShaderOutput> _path = new HashSet<ShaderOutput>(ReferenceEqualityComparer.Instance);
        private HashSet<ShaderGraphEdge> _visitedEdges = new HashSet<ShaderGraphEdge>();
    }
}
