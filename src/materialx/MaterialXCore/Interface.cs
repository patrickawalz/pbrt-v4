// MaterialX is Copyright Contributors to the MaterialX Project and is licensed
// under Apache-2.0. This file is a C# port of MaterialXCore/Interface.h and
// Interface.cpp.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MaterialX
{
    // PortElement Definition
    public abstract class PortElement : ValueElement
    {
        // PortElement Protected Methods
        protected PortElement(Element parent, string category, string name) : base(parent, category, name)
        {
        }

        // PortElement Public Methods
        public void setNodeName(string node)
        {
            setAttribute(NODE_NAME_ATTRIBUTE, node);
        }

        public bool hasNodeName()
        {
            return hasAttribute(NODE_NAME_ATTRIBUTE);
        }

        public string getNodeName()
        {
            return getAttribute(NODE_NAME_ATTRIBUTE);
        }

        public void setNodeGraphString(string node)
        {
            setAttribute(NODE_GRAPH_ATTRIBUTE, node);
        }

        public bool hasNodeGraphString()
        {
            return hasAttribute(NODE_GRAPH_ATTRIBUTE);
        }

        public string getNodeGraphString()
        {
            return getAttribute(NODE_GRAPH_ATTRIBUTE);
        }

        public void setOutputString(string output)
        {
            setAttribute(OUTPUT_ATTRIBUTE, output);
        }

        public bool hasOutputString()
        {
            return hasAttribute(OUTPUT_ATTRIBUTE);
        }

        public void setConnectedOutput(Output output)
        {
            if (output != null)
            {
                setOutputString(output.getName());
                Element parent = output.getParent();
                if (parent.isA<NodeGraph>())
                {
                    setNodeGraphString(parent.getName());
                    removeAttribute(NODE_NAME_ATTRIBUTE);
                }
                else if (parent.isA<Node>())
                {
                    setNodeName(parent.getName());
                    removeAttribute(NODE_GRAPH_ATTRIBUTE);
                }
                removeAttribute(VALUE_ATTRIBUTE);
            }
            else
            {
                removeAttribute(OUTPUT_ATTRIBUTE);
                removeAttribute(NODE_GRAPH_ATTRIBUTE);
                removeAttribute(NODE_NAME_ATTRIBUTE);
            }
        }

        public Output getConnectedOutput()
        {
            string outputString = getOutputString();
            Output result = null;

            // Determine the scope at which the connected output may be found.
            Element parent = getParent();
            Element scope = parent != null ? parent.getParent() : null;

            // Look for a nodegraph output.
            if (hasNodeGraphString())
            {
                NodeGraph nodeGraph = resolveNameReference<NodeGraph>(getNodeGraphString(), scope);
                if (nodeGraph == null)
                {
                    nodeGraph = resolveNameReference<NodeGraph>(getNodeGraphString());
                }
                if (nodeGraph != null)
                {
                    List<Output> outputs = nodeGraph.getOutputs();
                    if (outputs.Count != 0)
                    {
                        if (outputString.Length == 0)
                        {
                            result = outputs[0];
                        }
                        else
                        {
                            result = nodeGraph.getOutput(outputString);
                        }
                    }
                }
            }
            // Look for a node output.
            else if (hasNodeName())
            {
                string nodeName = getNodeName();
                Node node = resolveNameReference<Node>(nodeName, scope);
                if (node == null)
                {
                    node = resolveNameReference<Node>(nodeName);
                }
                if (node != null)
                {
                    List<Output> outputs = node.getOutputs();
                    if (outputs.Count != 0)
                    {
                        if (outputString.Length == 0)
                        {
                            result = outputs[0];
                        }
                        else
                        {
                            result = node.getOutput(outputString);
                        }
                    }
                }
            }

            // Look for an output at document scope.
            if (result == null)
            {
                result = getDocument().getOutput(outputString);
            }
            return result;
        }

        public string getOutputString()
        {
            return getAttribute(OUTPUT_ATTRIBUTE);
        }

        public void setConnectedNode(Node node)
        {
            if (node != null)
            {
                setNodeName(node.getName());
                removeAttribute(VALUE_ATTRIBUTE);
                removeAttribute(NODE_GRAPH_ATTRIBUTE);
                removeAttribute(INTERFACE_NAME_ATTRIBUTE);
            }
            else
            {
                removeAttribute(NODE_NAME_ATTRIBUTE);
            }
        }

        public virtual Node getConnectedNode()
        {
            GraphElement graph = getAncestorOfType<GraphElement>();
            return graph != null ? graph.getNode(getNodeName()) : null;
        }

        public override bool validate(StringBuilder message = null)
        {
            bool res = true;

            Node connectedNode = getConnectedNode();
            if (hasNodeName() || hasOutputString())
            {
                NodeGraph nodeGraph = resolveNameReference<NodeGraph>(getNodeName());
                if (nodeGraph == null)
                {
                    validateRequire(connectedNode != null, ref res, message, "Invalid port connection");
                }
            }
            if (connectedNode != null)
            {
                string outputString = getOutputString();
                if (outputString.Length != 0)
                {
                    Output output = null;
                    if (hasNodeName())
                    {
                        NodeDef nodeDef = connectedNode.getNodeDef();
                        output = nodeDef != null ? nodeDef.getOutput(outputString) : null;
                        if (output != null)
                        {
                            validateRequire(connectedNode.getType() == Mx.MULTI_OUTPUT_TYPE_STRING, ref res, message,
                                            "Multi-output type expected in port connection");
                        }
                    }
                    else if (hasNodeGraphString())
                    {
                        NodeGraph nodeGraph = resolveNameReference<NodeGraph>(getNodeGraphString());
                        if (nodeGraph != null)
                        {
                            output = nodeGraph.getOutput(outputString);
                            if (nodeGraph.getNodeDef() != null)
                            {
                                validateRequire(nodeGraph.getOutputCount() > 1, ref res, message,
                                                "Multi-output type expected in port connection");
                            }
                        }
                    }
                    else
                    {
                        output = getDocument().getOutput(outputString);
                    }
                    validateRequire(output != null, ref res, message, "No output found for port connection");

                    if (output != null)
                    {
                        validateRequire(getType() == output.getType(), ref res, message, "Mismatched types in port connection");
                    }
                }
                else if (connectedNode.getType() != Mx.MULTI_OUTPUT_TYPE_STRING)
                {
                    validateRequire(getType() == connectedNode.getType(), ref res, message, "Mismatched types in port connection");
                }
            }
            return base.validate(message) && res;
        }

        // PortElement Public Members
        public const string NODE_NAME_ATTRIBUTE = "nodename";
        public const string NODE_GRAPH_ATTRIBUTE = "nodegraph";
        public const string OUTPUT_ATTRIBUTE = "output";
    }

    // Input Definition
    public class Input : PortElement
    {
        // Input Public Methods
        public Input(Element parent, string name) : base(parent, CATEGORY, name)
        {
        }

        public void setDefaultGeomPropString(string geomprop)
        {
            setAttribute(DEFAULT_GEOM_PROP_ATTRIBUTE, geomprop);
        }

        public bool hasDefaultGeomPropString()
        {
            return hasAttribute(DEFAULT_GEOM_PROP_ATTRIBUTE);
        }

        public string getDefaultGeomPropString()
        {
            return getAttribute(DEFAULT_GEOM_PROP_ATTRIBUTE);
        }

        public GeomPropDef getDefaultGeomProp()
        {
            string defaultGeomProp = getAttribute(DEFAULT_GEOM_PROP_ATTRIBUTE);
            if (defaultGeomProp.Length != 0)
            {
                Document doc = getDocument();
                return doc.getChildOfType<GeomPropDef>(defaultGeomProp);
            }
            return null;
        }

        public override Node getConnectedNode()
        {
            // Traverse through interface names to nodegraph input
            Input graphInput = getInterfaceInput();
            if (graphInput != null && (graphInput.hasNodeName() || graphInput.hasNodeGraphString()))
            {
                return graphInput.getConnectedNode();
            }

            // Handle inputs of compound nodegraphs.
            if (getParent().isA<NodeGraph>())
            {
                Node rootNode = getDocument().getNode(getNodeName());
                if (rootNode != null)
                {
                    return rootNode;
                }
            }

            // Handle transitive connections via outputs.
            Output output = getConnectedOutput();
            if (output != null)
            {
                Node node = output.getConnectedNode();
                if (node != null)
                {
                    return node;
                }
            }

            return base.getConnectedNode();
        }

        public void setConnectedInterfaceName(string interfaceName)
        {
            if (interfaceName.Length != 0)
            {
                GraphElement graph = getAncestorOfType<GraphElement>();
                if (graph != null && graph == getParent())
                {
                    Element graphParent = graph.getParent();
                    graph = graphParent != null ? graphParent.getAncestorOfType<GraphElement>() : null;
                }
                if (graph != null && graph.getInput(interfaceName) != null)
                {
                    setInterfaceName(interfaceName);
                    removeAttribute(VALUE_ATTRIBUTE);
                    removeAttribute(OUTPUT_ATTRIBUTE);
                    removeAttribute(NODE_GRAPH_ATTRIBUTE);
                    removeAttribute(NODE_NAME_ATTRIBUTE);
                }
            }
            else
            {
                removeAttribute(INTERFACE_NAME_ATTRIBUTE);
            }
        }

        public Input getInterfaceInput()
        {
            if (hasInterfaceName())
            {
                GraphElement graph = getAncestorOfType<GraphElement>();
                if (graph != null)
                {
                    return graph.getInput(getInterfaceName());
                }
            }
            return null;
        }

        public bool hasHint()
        {
            return hasAttribute(HINT_ATTRIBUTE);
        }

        public string getHint()
        {
            return getAttribute(HINT_ATTRIBUTE);
        }

        public void setHint(string hint)
        {
            setAttribute(HINT_ATTRIBUTE, hint);
        }

        public override bool validate(StringBuilder message = null)
        {
            bool res = true;
            Element parent = getParent();

            if (hasDefaultGeomPropString())
            {
                validateRequire(parent.isA<NodeDef>() || parent.isA<NodeGraph>(), ref res, message, "Invalid defaultgeomprop on non-definition and non-nodegraph input");
                validateRequire(getDefaultGeomProp() != null, ref res, message, "Invalid defaultgeomprop string");
            }
            if (parent.isA<Node>())
            {
                int numBindings = 0;
                if (hasValue())
                {
                    numBindings++;
                }
                if (hasNodeName())
                {
                    numBindings++;
                }
                if (hasNodeGraphString())
                {
                    numBindings++;
                }
                if (hasInterfaceName())
                {
                    numBindings++;
                }
                if (hasOutputString() && !(hasNodeName() || hasNodeGraphString()))
                {
                    numBindings++;
                }

                validateRequire(numBindings != 0, ref res, message, "Node input binds no value or connection");
                validateRequire(numBindings <= 1, ref res, message, "Node input has too many bindings");
            }
            else if (parent.isA<NodeGraph>())
            {
                validateRequire(parent.asA<NodeGraph>().getNodeDef() == null, ref res, message, "Input element in a functional nodegraph has no effect");
            }
            return base.validate(message) && res;
        }

        // Input Public Members
        public const string CATEGORY = "input";
        public const string DEFAULT_GEOM_PROP_ATTRIBUTE = "defaultgeomprop";
        public const string HINT_ATTRIBUTE = "hint";
        public const string TRANSPARENCY_HINT = "transparency";
        public const string OPACITY_HINT = "opacity";
        public const string ANISOTROPY_HINT = "anisotropy";
    }

    // Output Definition
    public class Output : PortElement
    {
        // Output Public Methods
        public Output(Element parent, string name) : base(parent, CATEGORY, name)
        {
        }

        public override Edge getUpstreamEdge(int index = 0)
        {
            if (index < getUpstreamEdgeCount())
            {
                return new Edge(getSelfNonConst(), null, getConnectedNode());
            }

            return Mx.getNullEdge();
        }

        public override int getUpstreamEdgeCount()
        {
            return 1;
        }

        public bool hasUpstreamCycle()
        {
            try
            {
                foreach (Edge edge in traverseGraph())
                {
                }
            }
            catch (ExceptionFoundCycle)
            {
                return true;
            }
            return false;
        }

        public override bool validate(StringBuilder message = null)
        {
            bool res = true;
            validateRequire(!hasUpstreamCycle(), ref res, message, "Cycle in upstream path");
            return base.validate(message) && res;
        }

        // Output Public Members
        public const string CATEGORY = "output";
        public const string DEFAULT_INPUT_ATTRIBUTE = "defaultinput";
    }

    // InterfaceElement Definition
    public abstract class InterfaceElement : TypedElement
    {
        // InterfaceElement Protected Methods
        protected InterfaceElement(Element parent, string category, string name) : base(parent, category, name)
        {
            _inputCount = 0;
            _outputCount = 0;
        }

        // InterfaceElement Public Methods
        public void setNodeDefString(string nodeDef)
        {
            setAttribute(NODE_DEF_ATTRIBUTE, nodeDef);
        }

        public bool hasNodeDefString()
        {
            return hasAttribute(NODE_DEF_ATTRIBUTE);
        }

        public string getNodeDefString()
        {
            return getAttribute(NODE_DEF_ATTRIBUTE);
        }

        public Input addInput(string name = Mx.EMPTY_STRING, string type = Mx.DEFAULT_TYPE_STRING)
        {
            Input child = addChild<Input>(name);
            child.setType(type);
            return child;
        }

        public Input getInput(string name)
        {
            return getChildOfType<Input>(name);
        }

        public List<Input> getInputs()
        {
            return getChildrenOfType<Input>();
        }

        public int getInputCount()
        {
            return _inputCount;
        }

        public void removeInput(string name)
        {
            removeChildOfType<Input>(name);
        }

        public Input getActiveInput(string name)
        {
            if (!hasInheritString())
            {
                return getInput(name);
            }
            foreach (Element elem in traverseInheritance())
            {
                Input input = elem.asA<InterfaceElement>().getInput(name);
                if (input != null)
                {
                    return input;
                }
            }
            return null;
        }

        public List<Input> getActiveInputs()
        {
            if (!hasInheritString() && !isA<Document>())
            {
                return getInputs();
            }
            List<Input> activeInputs = new List<Input>();
            HashSet<string> activeInputNamesSet = new HashSet<string>(StringComparer.Ordinal);
            foreach (Element elem in traverseInheritance())
            {
                List<Input> inputs = elem.asA<InterfaceElement>().getInputs();
                foreach (Input input in inputs)
                {
                    if (input != null && activeInputNamesSet.Add(input.getName()))
                    {
                        activeInputs.Add(input);
                    }
                }
            }
            return activeInputs;
        }

        public Output addOutput(string name = Mx.EMPTY_STRING, string type = Mx.DEFAULT_TYPE_STRING)
        {
            Output output = addChild<Output>(name);
            output.setType(type);
            return output;
        }

        public Output getOutput(string name)
        {
            return getChildOfType<Output>(name);
        }

        public List<Output> getOutputs()
        {
            return getChildrenOfType<Output>();
        }

        public int getOutputCount()
        {
            return _outputCount;
        }

        public void removeOutput(string name)
        {
            removeChildOfType<Output>(name);
        }

        public Output getActiveOutput(string name)
        {
            if (!hasInheritString())
            {
                return getOutput(name);
            }
            foreach (Element elem in traverseInheritance())
            {
                Output output = elem.asA<InterfaceElement>().getOutput(name);
                if (output != null)
                {
                    return output;
                }
            }
            return null;
        }

        public List<Output> getActiveOutputs()
        {
            if (!hasInheritString() && !isA<Document>())
            {
                return getOutputs();
            }
            List<Output> activeOutputs = new List<Output>();
            HashSet<string> activeOutputNamesSet = new HashSet<string>(StringComparer.Ordinal);
            foreach (Element elem in traverseInheritance())
            {
                List<Output> outputs = elem.asA<InterfaceElement>().getOutputs();
                foreach (Output output in outputs)
                {
                    if (output != null && activeOutputNamesSet.Add(output.getName()))
                    {
                        activeOutputs.Add(output);
                    }
                }
            }
            return activeOutputs;
        }

        public void setConnectedOutput(string inputName, Output output)
        {
            Input input = getInput(inputName);
            if (input == null)
            {
                input = addInput(inputName);
            }
            if (output != null)
            {
                input.setType(output.getType());
            }
            input.setConnectedOutput(output);
        }

        public Output getConnectedOutput(string inputName)
        {
            Input input = getInput(inputName);
            if (input == null)
            {
                return null;
            }
            return input.getConnectedOutput();
        }

        public Token addToken(string name = Mx.EMPTY_STRING)
        {
            return addChild<Token>(name);
        }

        public Token getToken(string name)
        {
            return getChildOfType<Token>(name);
        }

        public List<Token> getTokens()
        {
            return getChildrenOfType<Token>();
        }

        public void removeToken(string name)
        {
            removeChildOfType<Token>(name);
        }

        public Token getActiveToken(string name)
        {
            if (!hasInheritString())
            {
                return getToken(name);
            }
            foreach (Element elem in traverseInheritance())
            {
                Token token = elem.asA<InterfaceElement>().getToken(name);
                if (token != null)
                {
                    return token;
                }
            }
            return null;
        }

        public List<Token> getActiveTokens()
        {
            if (!hasInheritString())
            {
                return getTokens();
            }
            List<Token> activeTokens = new List<Token>();
            foreach (Element elem in traverseInheritance())
            {
                List<Token> tokens = elem.asA<InterfaceElement>().getTokens();
                activeTokens.AddRange(tokens);
            }
            return activeTokens;
        }

        public ValueElement getValueElement(string name)
        {
            return getChildOfType<ValueElement>(name);
        }

        public ValueElement getActiveValueElement(string name)
        {
            if (!hasInheritString())
            {
                return getChildOfType<ValueElement>(name);
            }
            foreach (Element interfaceElement in traverseInheritance())
            {
                ValueElement valueElem = interfaceElement.getChildOfType<ValueElement>(name);
                if (valueElem != null)
                {
                    return valueElem;
                }
            }
            return null;
        }

        public List<ValueElement> getActiveValueElements()
        {
            if (!hasInheritString() && !isA<Document>())
            {
                return getChildrenOfType<ValueElement>();
            }
            List<ValueElement> activeValueElems = new List<ValueElement>();
            HashSet<string> activeValueElemNamesSet = new HashSet<string>(StringComparer.Ordinal);
            foreach (Element interfaceElement in traverseInheritance())
            {
                List<ValueElement> valueElems = interfaceElement.getChildrenOfType<ValueElement>();
                foreach (ValueElement valueElem in valueElems)
                {
                    if (valueElem != null && activeValueElemNamesSet.Add(valueElem.getName()))
                    {
                        activeValueElems.Add(valueElem);
                    }
                }
            }
            return activeValueElems;
        }

        public Input setInputValue<T>(string name, T value, string type = Mx.EMPTY_STRING)
        {
            Input input = getChildOfType<Input>(name);
            if (input == null)
            {
                input = addInput(name);
            }
            input.setValue(value, type);
            return input;
        }

        public Value getInputValue(string name, string target = Mx.EMPTY_STRING)
        {
            Input input = getInput(name);
            if (input != null)
            {
                return input.getValue();
            }

            // Return the value, if any, stored in our declaration.
            InterfaceElement decl = getDeclaration(target);
            if (decl != null)
            {
                input = decl.getInput(name);
                if (input != null)
                {
                    return input.getValue();
                }
            }

            return null;
        }

        public Token setTokenValue(string name, string value)
        {
            Token token = getToken(name);
            if (token == null)
            {
                token = addToken(name);
            }
            token.setValue<string>(value);
            return token;
        }

        public string getTokenValue(string name)
        {
            Token token = getToken(name);
            return token != null ? token.getValueString() : Mx.EMPTY_STRING;
        }

        public void setTarget(string target)
        {
            setAttribute(TARGET_ATTRIBUTE, target);
        }

        public bool hasTarget()
        {
            return hasAttribute(TARGET_ATTRIBUTE);
        }

        public string getTarget()
        {
            return getAttribute(TARGET_ATTRIBUTE);
        }

        public void setVersionString(string version)
        {
            setAttribute(VERSION_ATTRIBUTE, version);
        }

        public bool hasVersionString()
        {
            return hasAttribute(VERSION_ATTRIBUTE);
        }

        public string getVersionString()
        {
            return getAttribute(VERSION_ATTRIBUTE);
        }

        public void setVersionIntegers(int majorVersion, int minorVersion)
        {
            string versionString = majorVersion.ToString(CultureInfo.InvariantCulture) + "." +
                                   minorVersion.ToString(CultureInfo.InvariantCulture);
            setVersionString(versionString);
        }

        public virtual (int, int) getVersionIntegers()
        {
            string versionString = getVersionString();
            List<string> splitVersion = Mx.splitString(versionString, ".");
            int major;
            int minor;
            if (splitVersion.Count == 2)
            {
                if (Mx.stoi(splitVersion[0], out major) && Mx.stoi(splitVersion[1], out minor))
                {
                    return (major, minor);
                }
            }
            else if (splitVersion.Count == 1)
            {
                if (Mx.stoi(splitVersion[0], out major))
                {
                    return (major, 0);
                }
            }
            return (0, 0);
        }

        public void setDefaultVersion(bool defaultVersion)
        {
            setTypedAttribute<bool>(DEFAULT_VERSION_ATTRIBUTE, defaultVersion);
        }

        public bool getDefaultVersion()
        {
            return getTypedAttribute<bool>(DEFAULT_VERSION_ATTRIBUTE);
        }

        public virtual InterfaceElement getDeclaration(string target = Mx.EMPTY_STRING)
        {
            return null;
        }

        public override void clearContent()
        {
            _inputCount = 0;
            _outputCount = 0;
            base.clearContent();
        }

        public bool hasExactInputMatch(InterfaceElement declaration, StringBuilder message = null)
        {
            foreach (Input input in getActiveInputs())
            {
                Input declarationInput = declaration.getActiveInput(input.getName());
                if (declarationInput == null ||
                    declarationInput.getType() != input.getType())
                {
                    if (message != null)
                    {
                        message.Append("Input '" + input.getName() + "' doesn't match declaration");
                    }
                    return false;
                }
            }
            return true;
        }

        // InterfaceElement Protected Methods
        protected override void registerChildElement(Element child)
        {
            base.registerChildElement(child);
            if (child.isA<Input>())
            {
                _inputCount++;
            }
            else if (child.isA<Output>())
            {
                _outputCount++;
            }
        }

        protected override void unregisterChildElement(Element child)
        {
            base.unregisterChildElement(child);
            if (child.isA<Input>())
            {
                _inputCount--;
            }
            else if (child.isA<Output>())
            {
                _outputCount--;
            }
        }

        // InterfaceElement Public Members
        public const string NODE_DEF_ATTRIBUTE = "nodedef";
        public const string TARGET_ATTRIBUTE = "target";
        public const string VERSION_ATTRIBUTE = "version";
        public const string DEFAULT_VERSION_ATTRIBUTE = "isdefaultversion";

        // InterfaceElement Private Members
        private int _inputCount;
        private int _outputCount;
    }

    // Interface Functions
    public static partial class Mx
    {
        // std::stoi: white space is skipped and the longest integer prefix is
        // read. Returns false where std::stoi throws.
        public static bool stoi(string str, out int result)
        {
            long value;
            result = 0;
            if (!extractInteger(str, out value) || value > int.MaxValue || value < int.MinValue)
            {
                return false;
            }
            result = (int)value;
            return true;
        }
    }
}
