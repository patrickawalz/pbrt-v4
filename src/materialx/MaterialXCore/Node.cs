// MaterialX is Copyright Contributors to the MaterialX Project and is licensed
// under Apache-2.0. This file is a C# port of MaterialXCore/Node.h and Node.cpp.

using System;
using System.Collections.Generic;
using System.Text;

namespace MaterialX
{
    // Node Definition
    public class Node : InterfaceElement
    {
        // Node Public Methods
        public Node(Element parent, string name) : base(parent, CATEGORY, name)
        {
        }

        public void setNameGlobal(string name)
        {
            List<PortElement> downStreamPorts = getDownstreamPorts();
            setName(name);
            string newName = getName();
            foreach (PortElement port in downStreamPorts)
            {
                port.setNodeName(newName);
            }
        }

        public void setConnectedNode(string inputName, Node node)
        {
            Input input = getInput(inputName);
            if (input == null)
            {
                input = addInput(inputName);
            }
            if (node != null)
            {
                string type = node.getType();
                if (type != Mx.MULTI_OUTPUT_TYPE_STRING)
                {
                    input.setType(type);
                }
            }
            input.setConnectedNode(node);
        }

        public Node getConnectedNode(string inputName)
        {
            Input input = getInput(inputName);
            if (input == null)
            {
                return null;
            }
            return input.getConnectedNode();
        }

        public void setConnectedNodeName(string inputName, string nodeName)
        {
            Input input = getInput(inputName);
            if (input == null)
            {
                input = addInput(inputName);
            }
            input.setNodeName(nodeName);
        }

        public string getConnectedNodeName(string inputName)
        {
            Input input = getInput(inputName);
            if (input == null)
            {
                return Mx.EMPTY_STRING;
            }
            return input.getNodeName();
        }

        public NodeDef getNodeDef(string target = Mx.EMPTY_STRING, bool allowRoughMatch = false)
        {
            if (hasNodeDefString())
            {
                return resolveNameReference<NodeDef>(getNodeDefString());
            }
            List<NodeDef> nodeDefs = new List<NodeDef>(getDocument().getMatchingNodeDefs(getQualifiedName(getCategory())));
            List<NodeDef> secondary = getDocument().getMatchingNodeDefs(getCategory());
            List<NodeDef> roughMatches = new List<NodeDef>();
            nodeDefs.AddRange(secondary);
            foreach (NodeDef nodeDef in nodeDefs)
            {
                if (!Mx.targetStringsMatch(nodeDef.getTarget(), target) ||
                    !nodeDef.isVersionCompatible(getVersionString()) ||
                    nodeDef.getType() != getType())
                {
                    continue;
                }
                if (!hasExactInputMatch(nodeDef))
                {
                    if (allowRoughMatch)
                    {
                        roughMatches.Add(nodeDef);
                    }
                    continue;
                }
                return nodeDef;
            }
            if (roughMatches.Count != 0)
            {
                return roughMatches[0];
            }
            return null;
        }

        public InterfaceElement getImplementation(string target = Mx.EMPTY_STRING)
        {
            NodeDef nodeDef = getNodeDef(target);
            return nodeDef != null ? nodeDef.getImplementation(target) : null;
        }

        public override Edge getUpstreamEdge(int index = 0)
        {
            if (index < getUpstreamEdgeCount())
            {
                Input input = getInputs()[index];
                Element upstreamNode = input.getConnectedNode();
                if (upstreamNode != null)
                {
                    return new Edge(getSelfNonConst(), input, upstreamNode);
                }
            }

            return Mx.getNullEdge();
        }

        public override int getUpstreamEdgeCount()
        {
            return getInputCount();
        }

        public Output getNodeDefOutput(Element connectingElement)
        {
            string outputName = "";
            PortElement port = connectingElement.asA<PortElement>();
            if (port != null)
            {
                // The connecting element is an input/output port,
                // so get the name of the output connected to this
                // port. If no explicit output is specified this will
                // return an empty string which is handled below.
                outputName = port.getOutputString();

                // Handle case where it's an input to a top level output
                Input connectedInput = connectingElement.asA<Input>();
                Output output = null;
                if (connectedInput != null)
                {
                    Input interfaceInput = connectedInput.getInterfaceInput();
                    if (interfaceInput != null)
                    {
                        output = interfaceInput.getConnectedOutput();
                        outputName = interfaceInput.getOutputString();
                    }
                    else
                    {
                        output = connectedInput.getConnectedOutput();
                    }
                }
                if (output != null)
                {
                    if (connectedInput != null ||
                        output.getParent() == output.getDocument())
                    {
                        if (output.getOutputString().Length != 0)
                        {
                            outputName = output.getOutputString();
                        }
                    }
                }
            }
            if (outputName.Length != 0)
            {
                // Find this output on our nodedef.
                NodeDef nodeDef = getNodeDef();
                if (nodeDef != null)
                {
                    return nodeDef.getActiveOutput(outputName);
                }
            }
            return null;
        }

        public List<PortElement> getDownstreamPorts()
        {
            List<PortElement> downstreamPorts = new List<PortElement>();
            foreach (PortElement port in getDocument().getMatchingPorts(getQualifiedName(getName())))
            {
                if (port.getConnectedNode() == getSelf())
                {
                    downstreamPorts.Add(port);
                }
            }
            Mx.stdSort(downstreamPorts, (PortElement a, PortElement b) =>
            {
                return string.CompareOrdinal(a.getName(), b.getName()) > 0;
            });
            return downstreamPorts;
        }

        public override InterfaceElement getDeclaration(string target = Mx.EMPTY_STRING)
        {
            return getNodeDef(target);
        }

        public Input addInputFromNodeDef(string inputName)
        {
            Input nodeInput = getInput(inputName);
            if (nodeInput == null)
            {
                NodeDef nodeDef = getNodeDef();
                Input nodeDefInput = nodeDef != null ? nodeDef.getActiveInput(inputName) : null;
                if (nodeDefInput != null)
                {
                    nodeInput = addInput(nodeDefInput.getName(), nodeDefInput.getType());
                    if (nodeDefInput.hasValueString())
                    {
                        nodeInput.setValueString(nodeDefInput.getValueString());
                    }
                }
            }
            return nodeInput;
        }

        public void addInputsFromNodeDef()
        {
            NodeDef nodeDef = getNodeDef();
            if (nodeDef != null)
            {
                foreach (Input nodeDefInput in nodeDef.getActiveInputs())
                {
                    string inputName = nodeDefInput.getName();
                    Input nodeInput = getInput(inputName);
                    if (nodeInput == null)
                    {
                        nodeInput = addInput(inputName, nodeDefInput.getType());
                        if (nodeDefInput.hasValueString())
                        {
                            nodeInput.setValueString(nodeDefInput.getValueString());
                        }
                    }
                }
            }
        }

        public override bool validate(StringBuilder message = null)
        {
            bool res = true;
            validateRequire(getCategory().Length != 0, ref res, message, "Node element is missing a category");
            validateRequire(hasType(), ref res, message, "Node element is missing a type");

            NodeDef nodeDef = getNodeDef(Mx.EMPTY_STRING, true);
            if (nodeDef != null)
            {
                StringBuilder matchMessage = new StringBuilder();
                bool exactMatch = hasExactInputMatch(nodeDef, matchMessage);
                validateRequire(exactMatch, ref res, message, "Node interface error: " + matchMessage.ToString());

                List<Output> activeOutputs = nodeDef.getActiveOutputs();
                int numActiveOutputs = activeOutputs.Count;
                if (numActiveOutputs > 1)
                {
                    validateRequire(getType() == Mx.MULTI_OUTPUT_TYPE_STRING, ref res, message, "Node type is not 'multioutput' for node with multiple outputs");
                }
                else if (numActiveOutputs == 1)
                {
                    validateRequire(getType() == activeOutputs[0].getType(), ref res, message, "Node type does not match output port type");
                }
            }
            else
            {
                bool categoryDeclared = getDocument().getMatchingNodeDefs(getCategory()).Count != 0;
                validateRequire(!categoryDeclared, ref res, message, "Node interface doesn't support this output type");
            }

            return base.validate(message) && res;
        }

        // Node Public Members
        public const string CATEGORY = "node";
    }

    // GraphElement Definition
    public abstract class GraphElement : InterfaceElement
    {
        // GraphElement Protected Methods
        protected GraphElement(Element parent, string category, string name) : base(parent, category, name)
        {
        }

        // GraphElement Public Methods
        public Node addNode(string category, string name = Mx.EMPTY_STRING, string type = Mx.DEFAULT_TYPE_STRING)
        {
            Node node = addChild<Node>(name);
            node.setCategory(category);
            node.setType(type);
            return node;
        }

        public Node addNodeInstance(NodeDef nodeDef, string name = Mx.EMPTY_STRING)
        {
            Node node = addNode(nodeDef.getNodeString(), name, nodeDef.getType());
            node.setNodeDefString(nodeDef.getName());
            return node;
        }

        public Node getNode(string name)
        {
            return getChildOfType<Node>(name);
        }

        public List<Node> getNodes(string category = Mx.EMPTY_STRING)
        {
            return getChildrenOfType<Node>(category);
        }

        public List<Node> getNodesOfType(string nodeType)
        {
            List<Node> nodes = new List<Node>();
            foreach (Node node in getNodes())
            {
                if (node.getType() == nodeType)
                {
                    nodes.Add(node);
                }
            }
            return nodes;
        }

        public void removeNode(string name)
        {
            removeChildOfType<Node>(name);
        }

        public Node addMaterialNode(string name = Mx.EMPTY_STRING, Node shaderNode = null)
        {
            bool isVolumeShader = shaderNode != null && shaderNode.getType() == Mx.VOLUME_SHADER_TYPE_STRING;
            string category = isVolumeShader ? Mx.VOLUME_MATERIAL_NODE_STRING : Mx.SURFACE_MATERIAL_NODE_STRING;
            Node materialNode = addNode(category, name, Mx.MATERIAL_TYPE_STRING);
            if (shaderNode != null)
            {
                Input input = materialNode.addInput(shaderNode.getType(), shaderNode.getType());
                input.setConnectedNode(shaderNode);
            }
            return materialNode;
        }

        public List<Node> getMaterialNodes()
        {
            return getNodesOfType(Mx.MATERIAL_TYPE_STRING);
        }

        public Backdrop addBackdrop(string name = Mx.EMPTY_STRING)
        {
            return addChild<Backdrop>(name);
        }

        public Backdrop getBackdrop(string name)
        {
            return getChildOfType<Backdrop>(name);
        }

        public List<Backdrop> getBackdrops()
        {
            return getChildrenOfType<Backdrop>();
        }

        public void removeBackdrop(string name)
        {
            removeChildOfType<Backdrop>(name);
        }

        public void flattenSubgraphs(string target = Mx.EMPTY_STRING, Func<Node, bool> filter = null)
        {
            List<Node> nodeQueue = getNodes();
            while (nodeQueue.Count != 0)
            {
                // Determine which nodes require processing, and precompute declarations
                // and graph implementations for these nodes.
                List<Node> processNodeVec = new List<Node>();
                Dictionary<Node, NodeGraph> graphImplMap = new Dictionary<Node, NodeGraph>(ReferenceEqualityComparer.Instance);
                Dictionary<Node, InterfaceElement> declarationMap = new Dictionary<Node, InterfaceElement>(ReferenceEqualityComparer.Instance);
                Dictionary<Node, List<PortElement>> downstreamPortMap = new Dictionary<Node, List<PortElement>>(ReferenceEqualityComparer.Instance);
                foreach (Node node in nodeQueue)
                {
                    if (filter != null && !filter(node))
                    {
                        continue;
                    }
                    InterfaceElement implement = node.getImplementation(target);
                    if (implement != null && implement.isA<NodeGraph>())
                    {
                        processNodeVec.Add(node);
                        graphImplMap[node] = implement.asA<NodeGraph>();
                        declarationMap[node] = node.getDeclaration(target);
                        downstreamPortMap[node] = node.getDownstreamPorts();
                        foreach (Node sourceSubNode in implement.asA<NodeGraph>().getNodes())
                        {
                            downstreamPortMap[sourceSubNode] = sourceSubNode.getDownstreamPorts();
                        }
                    }
                }
                nodeQueue = new List<Node>();

                // Iterate through nodes with graph implementations.
                foreach (Node processNode in processNodeVec)
                {
                    NodeGraph sourceSubGraph = graphImplMap[processNode];
                    Dictionary<Node, Node> subNodeMap = new Dictionary<Node, Node>(ReferenceEqualityComparer.Instance);

                    // Create a new instance of each original subnode.
                    foreach (Node sourceSubNode in sourceSubGraph.getNodes())
                    {
                        string origName = sourceSubNode.getName();
                        string destName = createValidChildName(origName);
                        Node destSubNode = addNode(sourceSubNode.getCategory(), destName);

                        destSubNode.copyContentFrom(sourceSubNode);
                        setChildIndex(destSubNode.getName(), getChildIndex(processNode.getName()));

                        // Store the mapping between subgraphs.
                        subNodeMap[sourceSubNode] = destSubNode;

                        // Add the subnode to the queue, allowing processing of nested subgraphs.
                        nodeQueue.Add(destSubNode);
                    }

                    // Update properties of generated subnodes.
                    foreach (KeyValuePair<Node, Node> subNodePair in subNodeMap)
                    {
                        Node sourceSubNode = subNodePair.Key;
                        Node destSubNode = subNodePair.Value;

                        // Update downstream connections of this subnode.
                        foreach (PortElement sourcePort in portsOf(downstreamPortMap, sourceSubNode))
                        {
                            if (sourcePort.isA<Input>())
                            {
                                Node sourceParent = sourcePort.getParent().asA<Node>();
                                Node mapped;
                                if (sourceParent != null && subNodeMap.TryGetValue(sourceParent, out mapped))
                                {
                                    Input processNodeInput = mapped.getInput(sourcePort.getName());
                                    if (processNodeInput != null)
                                    {
                                        processNodeInput.setNodeName(destSubNode.getName());
                                    }
                                }
                            }
                            else if (sourcePort.isA<Output>())
                            {
                                foreach (PortElement processNodePort in portsOf(downstreamPortMap, processNode))
                                {
                                    processNodePort.setNodeName(destSubNode.getName());
                                }
                            }
                        }

                        // Transfer interface properties to this subnode.
                        foreach (Input destInput in destSubNode.getInputs())
                        {
                            if (destInput.hasInterfaceName())
                            {
                                Input sourceInput = processNode.getInput(destInput.getInterfaceName());
                                if (sourceInput != null)
                                {
                                    destInput.copyContentFrom(sourceInput);
                                    Node connectedNode = destInput.getConnectedNode();
                                    // Update downstream port map with the new instance
                                    if (connectedNode != null && downstreamPortMap.ContainsKey(connectedNode))
                                    {
                                        downstreamPortMap[connectedNode] = connectedNode.getDownstreamPorts();
                                    }
                                }
                                else
                                {
                                    InterfaceElement declaration;
                                    declarationMap.TryGetValue(processNode, out declaration);
                                    Input declInput = declaration != null ? declaration.getActiveInput(destInput.getInterfaceName()) : null;
                                    if (declInput != null)
                                    {
                                        if (declInput.hasValueString())
                                        {
                                            destInput.setValueString(declInput.getValueString());
                                        }
                                        if (declInput.hasDefaultGeomPropString())
                                        {
                                            GeomPropDef geomPropDef = getDocument().getGeomPropDef(declInput.getDefaultGeomPropString());
                                            if (geomPropDef != null)
                                            {
                                                destInput.setConnectedNode(addGeomNode(geomPropDef, "geomNode"));
                                            }
                                        }
                                    }
                                    destInput.removeAttribute(ValueElement.INTERFACE_NAME_ATTRIBUTE);
                                }
                            }
                        }
                    }

                    // Update downstream ports of the top-level node.
                    foreach (PortElement downstreamPort in portsOf(downstreamPortMap, processNode))
                    {
                        if (downstreamPort.hasOutputString())
                        {
                            Output subGraphOutput = sourceSubGraph.getOutput(downstreamPort.getOutputString());
                            if (subGraphOutput != null)
                            {
                                string destName = subGraphOutput.getNodeName();
                                Node sourceSubNode = sourceSubGraph.getNode(destName);
                                Node destNode = null;
                                if (sourceSubNode != null)
                                {
                                    subNodeMap.TryGetValue(sourceSubNode, out destNode);
                                }
                                if (destNode != null)
                                {
                                    destName = destNode.getName();
                                }
                                downstreamPort.setNodeName(destName);
                                downstreamPort.setOutputString(Mx.EMPTY_STRING);
                            }
                        }
                    }

                    // The processed node has been replaced, so remove it from the graph.
                    removeNode(processNode.getName());
                }
            }
        }

        public List<Element> topologicalSort()
        {
            // Calculate a topological order of the children, using Kahn's algorithm
            // to avoid recursion.
            //
            // Running time: O(numNodes + numEdges).

            List<Element> children = getChildren();

            // Calculate in-degrees for all children.
            Dictionary<Element, int> inDegree = new Dictionary<Element, int>(children.Count, ReferenceEqualityComparer.Instance);
            Queue<Element> childQueue = new Queue<Element>();
            foreach (Element child in children)
            {
                int connectionCount = 0;
                for (int i = 0; i < child.getUpstreamEdgeCount(); ++i)
                {
                    Edge upstreamEdge = child.getUpstreamEdge(i);
                    if (upstreamEdge)
                    {
                        if (upstreamEdge.getUpstreamElement() != null)
                        {
                            Element elem = upstreamEdge.getUpstreamElement().getParent();
                            if (elem == child.getParent())
                            {
                                connectionCount++;
                            }
                        }
                    }
                }

                inDegree[child] = connectionCount;

                // Enqueue children with in-degree 0.
                if (connectionCount == 0)
                {
                    childQueue.Enqueue(child);
                }
            }

            List<Element> result = new List<Element>();
            while (childQueue.Count != 0)
            {
                // Pop the queue and add to topological order.
                Element child = childQueue.Dequeue();
                result.Add(child);

                // Find connected nodes and decrease their in-degree,
                // adding node to the queue if in-degrees becomes 0.
                if (child.isA<Node>())
                {
                    foreach (PortElement port in child.asA<Node>().getDownstreamPorts())
                    {
                        Element downstreamElem = port.isA<Output>() ? port : port.getParent();
                        int degree;
                        inDegree.TryGetValue(downstreamElem, out degree);
                        if (degree > 1)
                        {
                            inDegree[downstreamElem] = degree - 1;
                        }
                        else
                        {
                            inDegree[downstreamElem] = 0;
                            childQueue.Enqueue(downstreamElem);
                        }
                    }
                }
            }

            return result;
        }

        public Node addGeomNode(GeomPropDef geomPropDef, string namePrefix)
        {
            string geomNodeName = namePrefix + "_" + geomPropDef.getName();
            Node geomNode = getNode(geomNodeName);
            if (geomNode == null)
            {
                geomNode = addNode(geomPropDef.getGeomProp(), geomNodeName, geomPropDef.getType());
                if (geomPropDef.hasAttribute(GeomPropDef.SPACE_ATTRIBUTE))
                {
                    geomNode.setInputValue(GeomPropDef.SPACE_ATTRIBUTE, geomPropDef.getAttribute(GeomPropDef.SPACE_ATTRIBUTE));
                }
                if (geomPropDef.hasAttribute(GeomPropDef.INDEX_ATTRIBUTE))
                {
                    geomNode.setInputValue(GeomPropDef.INDEX_ATTRIBUTE, geomPropDef.getAttribute(GeomPropDef.INDEX_ATTRIBUTE), Mx.getTypeString<int>());
                }
            }
            return geomNode;
        }

        public string asStringDot()
        {
            StringBuilder dot = new StringBuilder("digraph {\n");

            // Create a unique name for each child element.
            List<Element> children = topologicalSort();
            StringMap nameMap = new StringMap();
            StringSet nameSet = new StringSet();
            foreach (Element elem in children)
            {
                string uniqueName = elem.getCategory();
                while (nameSet.Contains(uniqueName))
                {
                    uniqueName = Mx.incrementName(uniqueName);
                }
                nameMap[elem.getName()] = uniqueName;
                nameSet.Add(uniqueName);
            }

            // Write out all nodes.
            foreach (Element elem in children)
            {
                Node node = elem.asA<Node>();
                if (node != null)
                {
                    dot.Append("    \"" + nameMap[node.getName()] + "\" ");
                    NodeDef nodeDef = node.getNodeDef();
                    string nodeGroup = nodeDef != null ? nodeDef.getNodeGroup() : Mx.EMPTY_STRING;
                    if (nodeGroup == NodeDef.CONDITIONAL_NODE_GROUP)
                    {
                        dot.Append("[shape=diamond];\n");
                    }
                    else
                    {
                        dot.Append("[shape=box];\n");
                    }
                }
            }

            // Write out all connections.
            HashSet<Edge> processedEdges = new HashSet<Edge>();
            StringSet processedInterfaces = new StringSet();
            foreach (Output output in getOutputs())
            {
                foreach (Edge edge in output.traverseGraph())
                {
                    if (!processedEdges.Contains(edge))
                    {
                        Element upstreamElem = edge.getUpstreamElement();
                        Element downstreamElem = edge.getDownstreamElement();
                        Element connectingElem = edge.getConnectingElement();

                        dot.Append("    \"" + nameOrEmpty(nameMap, upstreamElem.getName()));
                        dot.Append("\" -> \"" + nameOrEmpty(nameMap, downstreamElem.getName()));
                        dot.Append("\" [label=\"");
                        dot.Append(connectingElem != null ? connectingElem.getName() : Mx.EMPTY_STRING);
                        dot.Append("\"];\n");

                        Node upstreamNode = upstreamElem.asA<Node>();
                        if (upstreamNode != null && !processedInterfaces.Contains(upstreamNode.getName()))
                        {
                            foreach (Input input in upstreamNode.getInputs())
                            {
                                if (input.hasInterfaceName())
                                {
                                    dot.Append("    \"" + input.getInterfaceName());
                                    dot.Append("\" -> \"" + nameOrEmpty(nameMap, upstreamElem.getName()));
                                    dot.Append("\" [label=\"");
                                    dot.Append(input.getName());
                                    dot.Append("\"];\n");
                                }
                            }
                            processedInterfaces.Add(upstreamNode.getName());
                        }

                        processedEdges.Add(edge);
                    }
                }
            }

            dot.Append("}\n");

            return dot.ToString();
        }

        // GraphElement Private Methods
        private static List<PortElement> portsOf(Dictionary<Node, List<PortElement>> map, Node node)
        {
            List<PortElement> ports;
            if (!map.TryGetValue(node, out ports))
            {
                ports = new List<PortElement>();
                map[node] = ports;
            }
            return ports;
        }

        private static string nameOrEmpty(StringMap map, string key)
        {
            string value;
            if (!map.TryGetValue(key, out value))
            {
                value = Mx.EMPTY_STRING;
                map[key] = value;
            }
            return value;
        }
    }

    // NodeGraph Definition
    public class NodeGraph : GraphElement
    {
        // NodeGraph Public Methods
        public NodeGraph(Element parent, string name) : base(parent, CATEGORY, name)
        {
        }

        public void setNameGlobal(string name)
        {
            List<PortElement> downStreamPorts = getDownstreamPorts();
            setName(name);
            string newName = getName();
            foreach (PortElement port in downStreamPorts)
            {
                port.setNodeGraphString(newName);
            }
        }

        public List<Output> getMaterialOutputs()
        {
            List<Output> materialOutputs = new List<Output>();
            foreach (Output graphOutput in getActiveOutputs())
            {
                if (graphOutput.getType() == Mx.MATERIAL_TYPE_STRING)
                {
                    Node node = graphOutput.getConnectedNode();
                    if (node != null && node.getType() == Mx.MATERIAL_TYPE_STRING)
                    {
                        materialOutputs.Add(graphOutput);
                    }
                }
            }
            return materialOutputs;
        }

        public void setNodeDef(NodeDef nodeDef)
        {
            if (nodeDef != null)
            {
                setNodeDefString(nodeDef.getName());
            }
            else
            {
                removeAttribute(NODE_DEF_ATTRIBUTE);
            }
        }

        public NodeDef getNodeDef()
        {
            NodeDef nodedef = resolveNameReference<NodeDef>(getNodeDefString());
            // If not directly defined look for an implementation which has a nodedef association
            if (nodedef == null)
            {
                string qualifiedName = getQualifiedName(getName());
                foreach (Implementation impl in getDocument().getImplementations())
                {
                    if (impl.getNodeGraph() == qualifiedName)
                    {
                        nodedef = impl.getNodeDef();
                    }
                }
            }
            return nodedef;
        }

        public InterfaceElement getImplementation()
        {
            NodeDef nodedef = getNodeDef();
            return nodedef != null ? nodedef.getImplementation() : null;
        }

        public List<PortElement> getDownstreamPorts()
        {
            List<PortElement> downstreamPorts = new List<PortElement>();
            foreach (PortElement port in getDocument().getMatchingPorts(getQualifiedName(getName())))
            {
                Element node = port.getParent();
                Element graph = node != null ? node.getParent() : null;
                if (graph != null && graph.isA<GraphElement>() && graph == getParent())
                {
                    downstreamPorts.Add(port);
                }
            }
            Mx.stdSort(downstreamPorts, (PortElement a, PortElement b) =>
            {
                return string.CompareOrdinal(a.getName(), b.getName()) > 0;
            });
            return downstreamPorts;
        }

        public override InterfaceElement getDeclaration(string target = Mx.EMPTY_STRING)
        {
            NodeDef nodeDef = getNodeDef();
            if (nodeDef != null)
            {
                return nodeDef;
            }
            if (!hasNodeDefString())
            {
                return getSelf().asA<InterfaceElement>();
            }
            return null;
        }

        public Input addInterfaceName(string inputPath, string interfaceName)
        {
            NodeDef nodeDef = getNodeDef();
            InterfaceElement interfaceElement = nodeDef != null ? nodeDef.asA<InterfaceElement>() : getSelf().asA<InterfaceElement>();
            if (interfaceElement.getChild(interfaceName) != null)
            {
                throw new Exception("Interface: " + interfaceName + " has already been declared on the interface: " + interfaceElement.getNamePath());
            }

            Input interfaceInput = null;
            Element elem = getDescendant(inputPath);
            Input input = elem != null ? elem.asA<Input>() : null;
            if (input != null && input.getConnectedNode() == null)
            {
                input.setInterfaceName(interfaceName);
                interfaceInput = interfaceElement.getInput(interfaceName);
                if (interfaceInput == null)
                {
                    interfaceInput = interfaceElement.addInput(interfaceName, input.getType());
                }
                if (input.hasValue())
                {
                    interfaceInput.setValueString(input.getValueString());
                    input.removeAttribute(Input.VALUE_ATTRIBUTE);
                }
            }
            return interfaceInput;
        }

        public void removeInterfaceName(string inputPath)
        {
            Element desc = getDescendant(inputPath);
            Input input = desc != null ? desc.asA<Input>() : null;
            if (input != null)
            {
                string interfaceName = input.getInterfaceName();
                if (interfaceName.Length != 0)
                {
                    NodeDef nodeDef = getNodeDef();
                    InterfaceElement interfaceElement = nodeDef != null ? nodeDef.asA<InterfaceElement>() : getSelf().asA<InterfaceElement>();
                    Element interfacePort = interfaceElement.getChild(interfaceName);
                    if (interfacePort != null)
                    {
                        Input interfaceInput = interfacePort.asA<Input>();
                        if (interfaceInput != null && interfaceInput.hasValue())
                        {
                            input.setValueString(interfaceInput.getValueString());
                        }
                        interfaceElement.removeChild(interfaceName);
                    }
                    input.setInterfaceName(Mx.EMPTY_STRING);
                }
            }
        }

        public void modifyInterfaceName(string inputPath, string interfaceName)
        {
            NodeDef nodeDef = getNodeDef();
            InterfaceElement interfaceElement = nodeDef != null ? nodeDef.asA<InterfaceElement>() : getSelf().asA<InterfaceElement>();

            Element desc = getDescendant(inputPath);
            Input input = desc != null ? desc.asA<Input>() : null;
            if (input != null)
            {
                string previousName = input.getInterfaceName();
                if (previousName != interfaceName)
                {
                    Element previousChild = interfaceElement.getChild(previousName);
                    if (previousChild != null)
                    {
                        previousChild.setName(interfaceName);
                    }
                    input.setInterfaceName(interfaceName);
                }
            }
        }

        public override bool validate(StringBuilder message = null)
        {
            bool res = true;

            validateRequire(!hasVersionString(), ref res, message, "NodeGraph elements do not support version strings");
            if (hasNodeDefString())
            {
                NodeDef nodeDef = getNodeDef();
                validateRequire(nodeDef != null, ref res, message, "NodeGraph implementation refers to non-existent NodeDef");
                if (nodeDef != null)
                {
                    List<Output> graphOutputs = getOutputs();
                    List<Output> nodeDefOutputs = nodeDef.getActiveOutputs();
                    validateRequire(graphOutputs.Count == nodeDefOutputs.Count, ref res, message, "NodeGraph implementation has a different number of outputs than its NodeDef");
                    if (graphOutputs.Count == 1 && nodeDefOutputs.Count == 1)
                    {
                        validateRequire(graphOutputs[0].getType() == nodeDefOutputs[0].getType(), ref res, message, "NodeGraph implementation has a different output type than its NodeDef");
                    }
                }
            }

            return base.validate(message) && res;
        }

        // NodeGraph Public Members
        public const string CATEGORY = "nodegraph";
    }

    // Backdrop Definition
    public class Backdrop : Element
    {
        // Backdrop Public Methods
        public Backdrop(Element parent, string name) : base(parent, CATEGORY, name)
        {
        }

        public void setContainsString(string contains)
        {
            setAttribute(CONTAINS_ATTRIBUTE, contains);
        }

        public bool hasContainsString()
        {
            return hasAttribute(CONTAINS_ATTRIBUTE);
        }

        public string getContainsString()
        {
            return getAttribute(CONTAINS_ATTRIBUTE);
        }

        public void setWidth(float width)
        {
            setTypedAttribute<float>(WIDTH_ATTRIBUTE, width);
        }

        public bool hasWidth()
        {
            return hasAttribute(WIDTH_ATTRIBUTE);
        }

        public float getWidth()
        {
            return getTypedAttribute<float>(WIDTH_ATTRIBUTE);
        }

        public void setHeight(float height)
        {
            setTypedAttribute<float>(HEIGHT_ATTRIBUTE, height);
        }

        public bool hasHeight()
        {
            return hasAttribute(HEIGHT_ATTRIBUTE);
        }

        public float getHeight()
        {
            return getTypedAttribute<float>(HEIGHT_ATTRIBUTE);
        }

        public void setContainsElements(List<TypedElement> elems)
        {
            if (elems.Count != 0)
            {
                List<string> stringVec = new List<string>();
                foreach (TypedElement elem in elems)
                {
                    stringVec.Add(elem.getName());
                }
                setTypedAttribute(CONTAINS_ATTRIBUTE, stringVec);
            }
            else
            {
                removeAttribute(CONTAINS_ATTRIBUTE);
            }
        }

        public List<TypedElement> getContainsElements()
        {
            List<TypedElement> vec = new List<TypedElement>();
            GraphElement graph = getAncestorOfType<GraphElement>();
            if (graph != null)
            {
                foreach (string str in getTypedAttribute<List<string>>(CONTAINS_ATTRIBUTE))
                {
                    TypedElement elem = graph.getChildOfType<TypedElement>(str);
                    if (elem != null)
                    {
                        vec.Add(elem);
                    }
                }
            }
            return vec;
        }

        public override bool validate(StringBuilder message = null)
        {
            bool res = true;
            if (hasContainsString())
            {
                List<string> stringVec = getTypedAttribute<List<string>>("contains");
                List<TypedElement> elemVec = getContainsElements();
                validateRequire(stringVec.Count == elemVec.Count, ref res, message, "Invalid element in contains string");
            }
            return base.validate(message) && res;
        }

        // Backdrop Public Members
        public const string CATEGORY = "backdrop";
        public const string CONTAINS_ATTRIBUTE = "contains";
        public const string WIDTH_ATTRIBUTE = "width";
        public const string HEIGHT_ATTRIBUTE = "height";
    }
}
