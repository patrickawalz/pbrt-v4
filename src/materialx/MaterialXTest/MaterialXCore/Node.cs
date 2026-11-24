//
// Copyright Contributors to the MaterialX Project
// SPDX-License-Identifier: Apache-2.0
//

using System;
using System.Collections.Generic;
using System.Text;
using MaterialX;
using testing;
using static MaterialX.Mx;
using static MaterialXTest.Catch;

public static class CoreNodeTests
{
    public static bool isTopologicalOrder(List<Element> elems)
    {
        HashSet<Element> prevElems = new HashSet<Element>();
        foreach (Element elem in elems)
        {
            for (int i = 0; i < elem.getUpstreamEdgeCount(); i++)
            {
                Element upstreamElem = elem.getUpstreamElement(i);
                if (upstreamElem != null && !prevElems.Contains(upstreamElem))
                {
                    return false;
                }
            }
            prevElems.Add(elem);
        }
        return true;
    }

    [TEST("Core", "InterfaceInputValidation")]
    public static void Core_InterfaceInputValidation()
    {
        StringBuilder validationErrors = new StringBuilder();

        FileSearchPath searchPath = getDefaultDataSearchPath();
        Document doc = createDocument();
        loadLibraries(new List<FilePath> { new FilePath("libraries") }, searchPath, doc);

        // Test inside nodegraph
        GraphElement nodegraph = doc.addNodeGraph("graph1");

        List<GraphElement> graphs = new List<GraphElement> { doc, nodegraph };
        foreach (GraphElement graph in graphs)
        {
            Input graphInput = graph.addInput(EMPTY_STRING, "color3");
            Node addNode = graph.addNode("add", EMPTY_STRING, "color3");
            Input addInput = addNode.addInput("in1");

            addInput.setValueString("3, 3, 3");
            addInput.setInterfaceName(graphInput.getName());
            bool valid = doc.validate(validationErrors);
            if (!valid)
            {
                INFO(validationErrors.ToString());
            }
            REQUIRE(!valid);

            addInput.setConnectedInterfaceName(graphInput.getName());
            Input interfaceInput = addInput.getInterfaceInput();
            REQUIRE((interfaceInput != null && interfaceInput.getNamePath() == graphInput.getNamePath()));
            REQUIRE(addInput.getValue() == null);
            valid = doc.validate(validationErrors);
            if (!valid)
            {
                INFO(validationErrors.ToString());
            }
            REQUIRE(valid);

            addInput.setConnectedInterfaceName(EMPTY_STRING);
            addInput.setValueString("2, 2, 2");
            interfaceInput = addInput.getInterfaceInput();
            REQUIRE(interfaceInput == null);
            valid = doc.validate(validationErrors);
            if (!valid)
            {
                INFO(validationErrors.ToString());
            }
            REQUIRE(valid);
        }

        // Test that a nodegraph input can reference a document-level input
        // via its interface name.
        Input docInput = doc.addInput("doc_color", "color3");
        Input nodegraphInput = nodegraph.addInput("graph_color", "color3");
        nodegraphInput.setInterfaceName(docInput.getName());
        bool docValid = doc.validate(validationErrors);
        if (!docValid)
        {
            INFO(validationErrors.ToString());
        }
        REQUIRE(docValid);

        // Test that an invalid interface name on a nodegraph input is caught.
        nodegraphInput.setInterfaceName("invalid");
        docValid = doc.validate(validationErrors);
        REQUIRE(!docValid);

        // Test that a nodegraph input cannot reference a sibling input
        // via its interface name.
        Input graphInput2 = nodegraph.addInput("graph_color2", "color3");
        graphInput2.setInterfaceName("graph_color");
        docValid = doc.validate(validationErrors);
        REQUIRE(!docValid);
    }

    [TEST("Core", "NodeTypeMultioutputValidation")]
    public static void Core_NodeTypeMultioutputValidation()
    {
        // Create a document
        Document doc = createDocument();

        // Create a graph and add two outputs, types of the outputs are not important
        NodeGraph graph = doc.addNodeGraph("NG_custom_node");
        graph.addOutput("output1", "float");
        graph.addOutput("output2", "float");

        // Create a nodeDef based on the graph and make sure it has two outputs
        NodeDef nodeDef = doc.addNodeDefFromGraph(graph, "nodeDefName", "Category", "ND_custom_node");
        REQUIRE(nodeDef.getOutputCount() == 2);

        // Create a node based on the nodeDef and make sure it is of type multioutput and has no errors
        Node node = doc.addNodeInstance(nodeDef);
        REQUIRE(node.getType() == "multioutput");
        REQUIRE(node.validate());

        // Change the type of the node so that it does not match the nodeDef
        node.setType("float");
        // Make sure the validation fails as the node no longer has multioutput as type
        REQUIRE(!node.validate());
    }

    [TEST("Core", "NodeTypeValidation")]
    public static void Core_NodeTypeValidation()
    {
        // Create a document
        Document doc = createDocument();

        // Create a graph and add a single output
        NodeGraph graph = doc.addNodeGraph("NG_custom_node");
        graph.addOutput("output1", "float");

        // Create a nodeDef based on the graph and make sure it has a single output
        NodeDef nodeDef = doc.addNodeDefFromGraph(graph, "nodeDefName", "Category", "ND_custom_node");
        REQUIRE(nodeDef.getOutputCount() == 1);

        // Create a node based on the nodeDef and make sure it has no errors
        Node node = doc.addNodeInstance(nodeDef);
        REQUIRE(node.validate());

        // Change the type of the node so that it does not match the nodeDef
        node.setType("int");
        // Make sure the validation fails as the node has a different type than the single output of the nodedef
        REQUIRE(!node.validate());
    }

    [TEST("Core", "Node")]
    public static void Core_Node()
    {
        // Create a document.
        Document doc = createDocument();

        // Create a graph with two source nodes.
        Node constant = doc.addNode("constant");
        Node image = doc.addNode("image");
        REQUIRE(doc.getNodes().Count == 2);
        REQUIRE(doc.getNodes("constant").Count == 1);
        REQUIRE(doc.getNodes("image").Count == 1);

        // Set constant node color.
        Color3 color = new Color3(0.1f, 0.2f, 0.3f);
        constant.setInputValue<Color3>("value", color);
        REQUIRE(constant.getInputValue("value").isA<Color3>());
        REQUIRE(constant.getInputValue("value").asA<Color3>() == color);

        // Set image node file.
        string file = "image1.tif";
        image.setInputValue("file", file, FILENAME_TYPE_STRING);
        REQUIRE(image.getInputValue("file").isA<string>());
        REQUIRE(image.getInputValue("file").asA<string>() == file);

        // Create connected outputs.
        Output output1 = doc.addOutput();
        Output output2 = doc.addOutput();
        output1.setConnectedNode(constant);
        output2.setConnectedNode(image);
        REQUIRE(output1.getUpstreamElement() == constant);
        REQUIRE(output2.getUpstreamElement() == image);
        REQUIRE(constant.getDownstreamPorts()[0] == output1);
        REQUIRE(image.getDownstreamPorts()[0] == output2);

        // Create a custom nodedef.
        NodeDef customNodeDef = doc.addNodeDef("ND_turbulence3d", "float", "turbulence3d");
        customNodeDef.setNodeGroup(NodeDef.PROCEDURAL_NODE_GROUP);
        customNodeDef.setInputValue("octaves", 3);
        customNodeDef.setInputValue("lacunarity", 2.0f);
        customNodeDef.setInputValue("gain", 0.5f);

        // Reference the custom nodedef.
        Node custom = doc.addNodeInstance(customNodeDef);
        REQUIRE(custom.getNodeDefString() == customNodeDef.getName());
        REQUIRE(custom.getNodeDef().getNodeGroup() == NodeDef.PROCEDURAL_NODE_GROUP);
        REQUIRE(custom.getInputValue("octaves").isA<int>());
        REQUIRE(custom.getInputValue("octaves").asA<int>() == 3);
        custom.setInputValue("octaves", 5);
        REQUIRE(custom.getInputValue("octaves").asA<int>() == 5);

        // Remove the nodedef attribute from the node, requiring that it fall back
        // to type and version matching.
        custom.removeAttribute(NodeDef.NODE_DEF_ATTRIBUTE);
        REQUIRE(custom.getNodeDef() == customNodeDef);

        // Set nodedef and node version strings.
        customNodeDef.setVersionString("2.0");
        REQUIRE(custom.getNodeDef() == null);
        customNodeDef.setDefaultVersion(true);
        REQUIRE(custom.getNodeDef() == customNodeDef);
        custom.setVersionString("1");
        REQUIRE(custom.getNodeDef() == null);
        custom.removeAttribute(InterfaceElement.VERSION_ATTRIBUTE);
        REQUIRE(custom.getNodeDef() == customNodeDef);

        // Define a custom type.
        TypeDef typeDef = doc.addTypeDef("spectrum");
        int scalarCount = 10;
        for (int i = 0; i < scalarCount; i++)
        {
            Member scalar = typeDef.addMember();
            scalar.setType("float");
        }
        REQUIRE(typeDef.getMembers().Count == scalarCount);

        // Reference the custom type.
        string d65 = "{400;82.75;500;109.35;600;90.01;700;71.61;800;59.45}";
        constant.setInputValue<string>("value", d65, "spectrum");
        REQUIRE(constant.getInput("value").getType() == "spectrum");
        REQUIRE(constant.getInput("value").getValueString() == d65);
        REQUIRE(constant.getInputValue("value").isA<AggregateValue>());
        REQUIRE(constant.getInputValue("value").asA<AggregateValue>().getValueString() == d65);

        // Validate the document.
        REQUIRE(doc.validate());

        // Disconnect outputs from sources.
        output1.setConnectedNode(null);
        output2.setConnectedNode(null);
        REQUIRE(output1.getUpstreamElement() == null);
        REQUIRE(output2.getUpstreamElement() == null);
        REQUIRE(constant.getDownstreamPorts().Count == 0);
        REQUIRE(image.getDownstreamPorts().Count == 0);

        // Remove nodes and outputs.
        doc.removeNode(image.getName());
        doc.removeNode(constant.getName());
        doc.removeNode(custom.getName());
        doc.removeOutput(output1.getName());
        doc.removeOutput(output2.getName());
        REQUIRE(doc.getNodes().Count == 0);
        REQUIRE(doc.getOutputs().Count == 0);
    }

    [TEST("Core", "NodeInputCountRepro")]
    public static void Core_NodeInputCountRepro()
    {
        // Create a document.
        Document doc = createDocument();
        Node constant = doc.addNode("constant");
        constant.setInputValue<float>("value", 0.5f);

        // Check that input count is correct after clearContent
        constant.clearContent();
        CHECK(constant.getInputCount() == 0);

        // Check that validate succeeds after clear and rebuild
        constant.setType("float");
        Output output = doc.addOutput(EMPTY_STRING, "float");
        output.setConnectedNode(constant);
        CHECK(doc.validate());
    }

    [TEST("Core", "Flatten")]
    public static void Core_Flatten()
    {
        // Read an example containing graph-based custom nodes.
        FileSearchPath searchPath = getDefaultDataSearchPath();
        Document doc = createDocument();
        readFromXmlFile(doc, "resources/Materials/TestSuite/stdlib/shader/surface.mtlx", searchPath);
        REQUIRE(doc.validate());

        // Count root-level, nested, and custom nodes.
        int origRootNodes = doc.getNodes().Count;
        int origNestedNodes = 0;
        int origCustomNodes = 0;
        foreach (NodeGraph graph in doc.getNodeGraphs())
        {
            origNestedNodes += graph.getNodes().Count;
        }
        foreach (Node node in doc.getNodes())
        {
            if (node.getImplementation() != null)
            {
                origCustomNodes++;
            }
        }
        REQUIRE(origRootNodes > 0);
        REQUIRE(origNestedNodes > 0);
        REQUIRE(origCustomNodes > 0);

        // Flatten all root-level nodes.
        doc.flattenSubgraphs();
        REQUIRE(doc.validate());

        // Recount root-level nodes.
        int newRootNodes = doc.getNodes().Count;
        int expectedRootNodes = (origRootNodes - origCustomNodes) + (origNestedNodes * origCustomNodes);
        REQUIRE(newRootNodes == expectedRootNodes);
    }

    [TEST("Core", "Inheritance")]
    public static void Core_Inheritance()
    {
        FileSearchPath searchPath = getDefaultDataSearchPath();
        Document doc = createDocument();
        loadLibraries(new List<FilePath> { new FilePath("libraries") }, searchPath, doc);
        REQUIRE(doc.validate());
        NodeDef nodedef = doc.getNodeDef("ND_standard_surface_surfaceshader");
        REQUIRE(nodedef != null);
        Node surfaceNode = doc.addNodeInstance(nodedef);
        REQUIRE(surfaceNode != null);
        Input nodedefSpecularInput = nodedef.getActiveInput("specular");
        REQUIRE(nodedefSpecularInput != null);
        Input specularInput = surfaceNode.addInputFromNodeDef("specular");
        REQUIRE(specularInput != null);
        REQUIRE(specularInput.getAttribute(ValueElement.TYPE_ATTRIBUTE) ==
            nodedefSpecularInput.getAttribute(ValueElement.TYPE_ATTRIBUTE));
        REQUIRE(specularInput.getAttribute(ValueElement.VALUE_ATTRIBUTE) ==
            nodedefSpecularInput.getAttribute(ValueElement.VALUE_ATTRIBUTE));
    }

    [TEST("Core", "ActiveInterfaceInheritance")]
    public static void Core_ActiveInterfaceInheritance()
    {
        Document doc = createDocument();
        NodeGraph @base = doc.addNodeGraph("base");
        Input baseInput = @base.addInput("in", "float");
        Output baseOutput = @base.addOutput("out", "float");
        Token baseToken = @base.addToken("token");

        REQUIRE(@base.getActiveInput("in") == baseInput);
        REQUIRE(@base.getActiveOutput("out") == baseOutput);
        REQUIRE(@base.getActiveToken("token") == baseToken);
        REQUIRE(@base.getActiveValueElement("token") == baseToken);
        REQUIRE(vectorEqual(@base.getActiveInputs(), new List<Input> { baseInput }));
        REQUIRE(vectorEqual(@base.getActiveOutputs(), new List<Output> { baseOutput }));
        REQUIRE(vectorEqual(@base.getActiveTokens(), new List<Token> { baseToken }));
        REQUIRE(vectorEqual(@base.getActiveValueElements(), new List<ValueElement> { baseInput, baseOutput, baseToken }));
        REQUIRE(@base.getActiveInput("missing") == null);
        REQUIRE(@base.getActiveOutput("missing") == null);
        REQUIRE(@base.getActiveToken("missing") == null);
        REQUIRE(@base.getActiveValueElement("missing") == null);
        REQUIRE(!@base.hasInheritanceCycle());

        NodeGraph derived = doc.addNodeGraph("derived");
        derived.setInheritsFrom(@base);
        Input localInput = derived.addInput("in", "float");
        Token localToken = derived.addToken("token");
        REQUIRE(derived.getActiveInput("in") == localInput);
        REQUIRE(derived.getActiveOutput("out") == baseOutput);
        REQUIRE(derived.getActiveToken("token") == localToken);
        REQUIRE(vectorEqual(derived.getActiveInputs(), new List<Input> { localInput }));
        REQUIRE(vectorEqual(derived.getActiveOutputs(), new List<Output> { baseOutput }));
        // Token enumeration retains inherited tokens, including duplicate names.
        REQUIRE(vectorEqual(derived.getActiveTokens(), new List<Token> { localToken, baseToken }));
        REQUIRE(vectorEqual(derived.getActiveValueElements(), new List<ValueElement> { localInput, localToken, baseOutput }));

        derived.removeInput("in");
        REQUIRE(derived.getActiveInput("in") == baseInput);
        derived.setInheritsFrom(null);
        REQUIRE(derived.getActiveInput("in") == null);
        REQUIRE(derived.getActiveInputs().Count == 0);
        REQUIRE(derived.getActiveOutputs().Count == 0);
        REQUIRE(vectorEqual(derived.getActiveValueElements(), new List<ValueElement> { localToken }));

        derived.setInheritsFrom(@base);
        @base.setInheritsFrom(derived);
        REQUIRE(@base.hasInheritanceCycle());
        REQUIRE_THROWS_AS<ExceptionFoundCycle>(() => { derived.getActiveInputs(); });
        REQUIRE_THROWS_AS<ExceptionFoundCycle>(() => { derived.getActiveOutputs(); });
        REQUIRE_THROWS_AS<ExceptionFoundCycle>(() => { derived.getActiveTokens(); });
        REQUIRE_THROWS_AS<ExceptionFoundCycle>(() => { derived.getActiveValueElements(); });
        @base.setInheritsFrom(null);
        REQUIRE(!derived.hasInheritanceCycle());

        // Documents enumerate data-library children before local children, and
        // active input/output/value enumeration must still remove duplicate names.
        Document library = createDocument();
        Input libraryInput = library.addInput("in", "float");
        Output libraryOutput = library.addOutput("out", "float");
        doc.addInput("in", "float");
        doc.addOutput("out", "float");
        doc.setDataLibrary(library);
        REQUIRE(vectorEqual(doc.getActiveInputs(), new List<Input> { libraryInput }));
        REQUIRE(vectorEqual(doc.getActiveOutputs(), new List<Output> { libraryOutput }));
        REQUIRE(vectorEqual(doc.getActiveValueElements(), new List<ValueElement> { libraryInput, libraryOutput }));
    }

    [TEST("Core", "NodegraphImplementationLookup")]
    public static void Core_NodegraphImplementationLookup()
    {
        Document library = createDocument();
        NodeDef first = library.addNodeDef("first", "float");
        NodeDef second = library.addNodeDef("second", "float");
        Implementation impl = library.addImplementation("impl");
        impl.setNodeGraph("test:graph");
        impl.setNodeDef(first);

        Document doc = createDocument();
        doc.setDataLibrary(library);
        NodeGraph graph = doc.addNodeGraph("graph");
        REQUIRE(graph.getNodeDef() == null);
        graph.setNamespace("test");
        REQUIRE(graph.getNodeDef() == first);
        Implementation localImpl = doc.addImplementation("local_impl");
        localImpl.setNodeGraph("test:graph");
        localImpl.setNodeDef(second);
        REQUIRE(graph.getNodeDef() == second);
        graph.setNodeDef(first);
        REQUIRE(graph.getNodeDef() == first);
        graph.removeAttribute(InterfaceElement.NODE_DEF_ATTRIBUTE);
        graph.setNamespace("other");
        REQUIRE(graph.getNodeDef() == null);
    }

    [TEST("Core", "TopologicalSort")]
    public static void Core_TopologicalSort()
    {
        // Create a document.
        Document doc = createDocument();

        // Create a node graph with the following structure:
        //
        //   [constant1] [constant2]      [image2]
        //           \   /          \    /
        // [image1] [add1]          [add2]
        //        \  /   \______      |   
        //    [multiply]        \__ [add3]         [noise3d]
        //             \____________  |  ____________/
        //                          [mix]
        //                            |
        //                         [output]
        //
        NodeGraph nodeGraph = doc.addNodeGraph();
        Node image1 = nodeGraph.addNode("image");
        Node image2 = nodeGraph.addNode("image");
        Node multiply = nodeGraph.addNode("multiply");
        Node constant1 = nodeGraph.addNode("constant");
        Node constant2 = nodeGraph.addNode("constant");
        Node add1 = nodeGraph.addNode("add");
        Node add2 = nodeGraph.addNode("add");
        Node add3 = nodeGraph.addNode("add");
        Node noise3d = nodeGraph.addNode("noise3d");
        Node mix = nodeGraph.addNode("mix");
        Output output = nodeGraph.addOutput();
        add1.setConnectedNode("in1", constant1);
        add1.setConnectedNode("in2", constant2);
        add2.setConnectedNode("in1", constant2);
        add2.setConnectedNode("in2", image2);
        add3.setConnectedNode("in1", add1);
        add3.setConnectedNode("in2", add2);
        multiply.setConnectedNode("in1", image1);
        multiply.setConnectedNode("in2", add1);
        mix.setConnectedNode("fg", multiply);
        mix.setConnectedNode("bg", add3);
        mix.setConnectedNode("mask", noise3d);
        output.setConnectedNode(mix);

        // Validate the document.
        REQUIRE(doc.validate());

        // Create a topological order and validate the results.
        List<Element> elemOrder = nodeGraph.topologicalSort();
        REQUIRE(elemOrder.Count == nodeGraph.getChildren().Count);
        REQUIRE(isTopologicalOrder(elemOrder));
    }

    [TEST("Core", "NewNodegraphFromOutput")]
    public static void Core_NewNodegraphFromOutput()
    {
        // Create a document.
        Document doc = createDocument();

        // Create a node graph with the following structure:
        //
        //   [constant1] [constant2]      [image2]
        //           \   /          \    /
        // [image1] [add1]          [add2]
        //        \  /   \______      |   
        //   [multiply1]        \__ [add3]         [noise3d]            [constant3]
        //             \____________  |  ____________/    \                /
        //                          [mix]                  \_ [multiply2]_/
        //                            |                           |
        //                          [out1]                      [out2]
        //
        NodeGraph nodeGraph = doc.addNodeGraph();
        Node image1 = nodeGraph.addNode("image");
        Node image2 = nodeGraph.addNode("image");
        Node multiply1 = nodeGraph.addNode("multiply");
        Node multiply2 = nodeGraph.addNode("multiply");
        Node constant1 = nodeGraph.addNode("constant");
        Node constant2 = nodeGraph.addNode("constant");
        Node constant3 = nodeGraph.addNode("constant");
        Node add1 = nodeGraph.addNode("add");
        Node add2 = nodeGraph.addNode("add");
        Node add3 = nodeGraph.addNode("add");
        Node noise3d = nodeGraph.addNode("noise3d");
        Node mix = nodeGraph.addNode("mix");
        Output out1 = nodeGraph.addOutput("out1");
        Output out2 = nodeGraph.addOutput("out2");
        add1.setConnectedNode("in1", constant1);
        add1.setConnectedNode("in2", constant2);
        add2.setConnectedNode("in1", constant2);
        add2.setConnectedNode("in2", image2);
        add3.setConnectedNode("in1", add1);
        add3.setConnectedNode("in2", add2);
        multiply1.setConnectedNode("in1", image1);
        multiply1.setConnectedNode("in2", add1);
        multiply2.setConnectedNode("in1", noise3d);
        multiply2.setConnectedNode("in2", constant3);
        mix.setConnectedNode("fg", multiply1);
        mix.setConnectedNode("bg", add3);
        mix.setConnectedNode("mask", noise3d);
        out1.setConnectedNode(mix);
        out2.setConnectedNode(multiply2);

        // Generate a new graph from each output.
        List<Output> outputs = new List<Output> { out1, out2 };
        for (int i = 0; i < outputs.Count; ++i)
        {
            Output output = outputs[i];

            // Create a new graph with this output.
            NodeGraph nodeGraph2 = doc.addNodeGraph();
            nodeGraph2.addOutput(output.getName());

            // Keep track of processed nodes to avoid duplication
            // of nodes with multiple downstream connections.
            HashSet<Node> processedNodes = new HashSet<Node>();

            foreach (Edge edge in output.traverseGraph())
            {
                Node upstreamNode = edge.getUpstreamElement().asA<Node>();
                if (processedNodes.Contains(upstreamNode))
                {
                    // Node is already processed
                    continue;
                }

                // Create this node in the new graph.
                Node newNode = nodeGraph2.addNode(upstreamNode.getCategory(), upstreamNode.getName());
                newNode.copyContentFrom(upstreamNode);

                // Connect the node to downstream element in the new graph.
                Element downstreamElement = edge.getDownstreamElement();
                Element connectingElement = edge.getConnectingElement();
                if (downstreamElement.isA<Output>())
                {
                    Output downstream = nodeGraph2.getOutput(downstreamElement.getName());
                    downstream.setConnectedNode(newNode);
                }
                else if (connectingElement != null)
                {
                    Node downstream = nodeGraph2.getNode(downstreamElement.getName());
                    downstream.setConnectedNode(connectingElement.getName(), newNode);
                }

                // Mark node as processed.
                processedNodes.Add(upstreamNode);
            }

            // Create a topological order and validate the results.
            List<Element> elemOrder = nodeGraph2.topologicalSort();
            REQUIRE(elemOrder.Count == nodeGraph2.getChildren().Count);
            REQUIRE(isTopologicalOrder(elemOrder));
        }

        // Validate the document.
        REQUIRE(doc.validate());
    }

    [TEST("Core", "PruneNodes")]
    public static void Core_PruneNodes()
    {
        // Create a document.
        Document doc = createDocument();

        // Create a node graph with the following structure:
        //
        //   [constant1] [constant2]      [image2]
        //           \   /          \    /
        // [image1] [add1]          [add2]
        //        \  /   \______      |   
        //    [multiply]        \__ [add3]         [noise3d]
        //             \____________  |  ____________/
        //                          [mix]
        //                            |
        //                         [output]
        //
        NodeGraph nodeGraph = doc.addNodeGraph();
        Node image1 = nodeGraph.addNode("image");
        Node image2 = nodeGraph.addNode("image");
        Node multiply = nodeGraph.addNode("multiply");
        Node constant1 = nodeGraph.addNode("constant");
        Node constant2 = nodeGraph.addNode("constant");
        Node add1 = nodeGraph.addNode("add");
        Node add2 = nodeGraph.addNode("add");
        Node add3 = nodeGraph.addNode("add");
        Node noise3d = nodeGraph.addNode("noise3d");
        Node mix = nodeGraph.addNode("mix");
        Output output = nodeGraph.addOutput();
        add1.setConnectedNode("in1", constant1);
        add1.setConnectedNode("in2", constant2);
        add2.setConnectedNode("in1", constant2);
        add2.setConnectedNode("in2", image2);
        add3.setConnectedNode("in1", add1);
        add3.setConnectedNode("in2", add2);
        multiply.setConnectedNode("in1", image1);
        multiply.setConnectedNode("in2", add1);
        mix.setConnectedNode("fg", multiply);
        mix.setConnectedNode("bg", add3);
        mix.setConnectedNode("mask", noise3d);
        output.setConnectedNode(mix);

        // Set the node names we want to prune from the graph
        // and which corresponding input to use for the bypass.
        Dictionary<string, string> nodesToPrune = new Dictionary<string, string>
        {
            { "add1","in1" },
            { "add2","in1" },
            { "add3","in1" }
        };

        // Keep track of processed nodes to avoid duplication
        // of nodes with multiple downstream connections.
        HashSet<Node> processedNodes = new HashSet<Node>();

        // Create the new graph with this output and traverse the
        // original graph upstream to find which nodes to copy.
        NodeGraph nodeGraph2 = doc.addNodeGraph();
        nodeGraph2.addOutput(output.getName());

        foreach (Edge edge in output.traverseGraph())
        {
            Node upstreamNode = edge.getUpstreamElement().asA<Node>();
            if (processedNodes.Contains(upstreamNode))
            {
                // Node is already processed.
                continue;
            }

            // Find the downstream element in the new graph.
            Element downstreamElement = edge.getDownstreamElement();
            Element downstreamElement2 = nodeGraph2.getChild(downstreamElement.getName());
            if (downstreamElement2 == null)
            {
                // Downstream element has been pruned
                // so ignore this edge.
                continue;
            }

            // Check if this node should be pruned.
            // If so we travers upstream using the bypass inputs
            // until a non-prune node is found.
            Value value = null;
            while (upstreamNode != null)
            {
                if (!nodesToPrune.ContainsKey(upstreamNode.getName()))
                {
                    break;
                }
                string inputName = nodesToPrune[upstreamNode.getName()];
                upstreamNode = upstreamNode.getConnectedNode(inputName);
            }

            if (upstreamNode != null)
            {
                // Get (or create) the node in the new graph.
                Node upstreamNode2 = nodeGraph2.getNode(upstreamNode.getName());
                if (upstreamNode2 == null)
                {
                    upstreamNode2 = nodeGraph2.addNode(upstreamNode.getCategory(), upstreamNode.getName());
                    upstreamNode2.copyContentFrom(upstreamNode);
                }

                Element connectingElement = edge.getConnectingElement();

                // Connect it to downstream.
                // The downstream element could be a node or an output.
                Node downstreamNode2 = downstreamElement2.asA<Node>();
                Output downstreamOutput2 = downstreamElement2.asA<Output>();
                if (downstreamOutput2 != null)
                {
                    downstreamOutput2.setConnectedNode(upstreamNode2);
                }
                else if (downstreamNode2 != null && connectingElement != null)
                {
                    downstreamNode2.setConnectedNode(connectingElement.getName(), upstreamNode2);
                }
            }

            // Mark node as processed.
            processedNodes.Add(upstreamNode);
        }

        // Validate the document.
        REQUIRE(doc.validate());

        // Create a topological order and validate the results.
        List<Element> elemOrder = nodeGraph2.topologicalSort();
        REQUIRE(elemOrder.Count == nodeGraph2.getChildren().Count);
        REQUIRE(isTopologicalOrder(elemOrder));
    }

    [TEST("Core", "Organization")]
    public static void Core_Organization()
    {
        // Create a document.
        Document doc = createDocument();

        // Create a node graph with the following structure:
        //
        //   [constant1] [constant2]      [image2]
        //           \   /          \    /
        // [image1] [add1]          [add2]
        //        \  /   \______      |   
        //    [multiply]        \__ [add3]         [noise3d]
        //             \____________  |  ____________/
        //                          [mix]
        //                            |
        //                         [output]
        //
        NodeGraph nodeGraph = doc.addNodeGraph();
        Node image1 = nodeGraph.addNode("image");
        Node image2 = nodeGraph.addNode("image");
        Node multiply = nodeGraph.addNode("multiply");
        Node constant1 = nodeGraph.addNode("constant");
        Node constant2 = nodeGraph.addNode("constant");
        Node add1 = nodeGraph.addNode("add");
        Node add2 = nodeGraph.addNode("add");
        Node add3 = nodeGraph.addNode("add");
        Node noise3d = nodeGraph.addNode("noise3d");
        Node mix = nodeGraph.addNode("mix");
        Output output = nodeGraph.addOutput();
        add1.setConnectedNode("in1", constant1);
        add1.setConnectedNode("in2", constant2);
        add2.setConnectedNode("in1", constant2);
        add2.setConnectedNode("in2", image2);
        add3.setConnectedNode("in1", add1);
        add3.setConnectedNode("in2", add2);
        multiply.setConnectedNode("in1", image1);
        multiply.setConnectedNode("in2", add1);
        mix.setConnectedNode("fg", multiply);
        mix.setConnectedNode("bg", add3);
        mix.setConnectedNode("mask", noise3d);
        output.setConnectedNode(mix);

        // Create a backdrop element.
        Backdrop backdrop1 = nodeGraph.addBackdrop();
        backdrop1.setContainsElements(new List<TypedElement> { constant1, constant2, add1 });
        backdrop1.setDocString("Group 1");
        backdrop1.setWidth(10.0f);
        backdrop1.setHeight(20.0f);
        CHECK(backdrop1.getContainsElements().Count == 3);
        CHECK(backdrop1.getContainsElements()[0] == constant1);
        CHECK(backdrop1.getDocString() == "Group 1");
        CHECK(backdrop1.getWidth() == 10.0f);
        CHECK(backdrop1.getHeight() == 20.0f);

        // Create a second backdrop element.
        Backdrop backdrop2 = nodeGraph.addBackdrop();
        backdrop2.setContainsElements(new List<TypedElement> { multiply, noise3d, mix, output });
        backdrop2.setDocString("Group 2");
        backdrop2.setWidth(30.0f);
        backdrop2.setHeight(40.0f);
        CHECK(backdrop2.getContainsElements().Count == 4);
        CHECK(backdrop2.getContainsElements()[0] == multiply);
        CHECK(backdrop2.getDocString() == "Group 2");
        CHECK(backdrop2.getWidth() == 30.0f);
        CHECK(backdrop2.getHeight() == 40.0f);

        // Validate the document.
        REQUIRE(doc.validate());

        // Create and test an invalid contains element.
        backdrop2.setContainsElements(new List<TypedElement> { nodeGraph });
        REQUIRE(!doc.validate());

        // Remove backdrops.
        nodeGraph.removeBackdrop(backdrop1.getName());
        nodeGraph.removeBackdrop(backdrop2.getName());
        CHECK(nodeGraph.getBackdrops().Count == 0);
    }

    [TEST("Core", "NodeDefinitionCreation")]
    public static void Core_NodeDefinitionCreation()
    {
        FileSearchPath searchPath = getDefaultDataSearchPath();
        Document stdlib = createDocument();
        loadLibraries(new List<FilePath> { new FilePath("libraries") }, searchPath, stdlib);

        Document doc = createDocument();
        readFromXmlFile(doc, "resources/Materials/TestSuite/stdlib/definition/definition_from_nodegraph.mtlx", searchPath);
        doc.setDataLibrary(stdlib);

        NodeGraph graph = doc.getNodeGraph("test_colorcorrect");
        REQUIRE(graph != null);
        if (graph != null)
        {
            // Add some input interfaces to the graph
            foreach (Node node in graph.getNodes())
            {
                foreach (Input input in node.getInputs())
                {
                    if (input.getConnectedNode() == null)
                    {
                        string relativePath = node.getName() + "/" + input.getName();
                        string interfaceName = graph.createValidChildName(relativePath);
                        Input interfaceInput = graph.addInterfaceName(relativePath, interfaceName);
                        REQUIRE(interfaceInput != null);
                        if (interfaceInput != null)
                        {
                            interfaceInput.setAttribute(PortElement.UI_NAME_ATTRIBUTE, node.getName() + " " + input.getName());
                            interfaceInput.setAttribute(PortElement.UI_FOLDER_ATTRIBUTE, "Common");
                        }
                    }
                }
            }

            string VERSION1 = "1.0";
            string GROUP = "adjustment";
            bool isDefaultVersion = false;
            string NODENAME = graph.getName();

            // Create a new functional graph and definition from a compound graph
            string newNodeDefName = doc.createValidChildName("ND_" + graph.getName());
            string newGraphName = doc.createValidChildName("NG_" + graph.getName());
            NodeDef nodeDef = doc.addNodeDefFromGraph(graph, newNodeDefName, NODENAME, newGraphName);
            REQUIRE(nodeDef != null);
            nodeDef.setVersionString(VERSION1);
            nodeDef.setDefaultVersion(isDefaultVersion);
            nodeDef.setNodeGroup(GROUP);
            nodeDef.setAttribute(PortElement.UI_NAME_ATTRIBUTE, NODENAME + " Version: " + VERSION1);
            nodeDef.setDocString("This is version 1 of the definition for the graph: " + newGraphName);

            // Check validity of new definition
            REQUIRE(nodeDef.getNodeGroup() == "adjustment");
            REQUIRE(nodeDef.getVersionString() == VERSION1);
            REQUIRE_FALSE(nodeDef.getDefaultVersion());
            foreach (Input origInput in graph.getInputs())
            {
                Input nodeDefInput = nodeDef.getInput(origInput.getName());
                REQUIRE(nodeDefInput != null);
                REQUIRE(origInput.isEqual(nodeDefInput));
            }
            StringSet connectionAttributes = new StringSet
            {
                PortElement.NODE_GRAPH_ATTRIBUTE,
                PortElement.NODE_NAME_ATTRIBUTE,
                PortElement.INTERFACE_NAME_ATTRIBUTE,
                Element.XPOS_ATTRIBUTE,
                Element.YPOS_ATTRIBUTE
            };
            foreach (Output origOutput in graph.getOutputs())
            {
                Output nodeDefOutput = nodeDef.getOutput(origOutput.getName());
                REQUIRE(nodeDefOutput != null);
                foreach (string attribName in origOutput.getAttributeNames())
                {
                    if (connectionAttributes.Contains(attribName))
                    {
                        REQUIRE(!nodeDefOutput.hasAttribute(attribName));
                        continue;
                    }
                    REQUIRE(origOutput.getAttribute(attribName) == nodeDefOutput.getAttribute(attribName));
                }
            }

            // Check validity of new functional nodegraph
            NodeGraph newGraph = doc.getNodeGraph(newGraphName);
            REQUIRE(newGraph != null);
            REQUIRE(newGraph.getNodeDefString() == newNodeDefName);
            InterfaceElement decl = newGraph.getDeclaration();
            REQUIRE(decl.getName() == nodeDef.getName());
            REQUIRE(doc.validate());

            // Create the new node
            Node newInstance = doc.addNode(NODENAME, EMPTY_STRING, MULTI_OUTPUT_TYPE_STRING);
            REQUIRE(newInstance != null);

            // Remove default version attribute from previous definitions
            foreach (NodeDef prevNodeDef in doc.getMatchingNodeDefs(NODENAME))
            {
                prevNodeDef.setDefaultVersion(false);
            }

            // Add new version 
            string VERSION2 = "2.0";
            newGraphName = EMPTY_STRING;
            newNodeDefName = doc.createValidChildName("ND_" + graph.getName() + "_2");
            newGraphName = doc.createValidChildName("NG_" + graph.getName() + "_2");

            // Create new default version
            nodeDef = doc.addNodeDefFromGraph(graph, newNodeDefName + "2", NODENAME, newGraphName);
            nodeDef.setVersionString(VERSION2);
            nodeDef.setNodeGroup(GROUP);
            nodeDef.setDefaultVersion(true);
            REQUIRE(nodeDef != null);
            nodeDef.setAttribute(PortElement.UI_NAME_ATTRIBUTE, NODENAME + " Version: " + VERSION2);
            nodeDef.setDocString("This is version 2 of the definition for the graph: " + newGraphName);

            // Check that we create the version by default
            Node newDefault = doc.addNode(NODENAME, EMPTY_STRING, MULTI_OUTPUT_TYPE_STRING);
            if (newDefault != null)
            {
                nodeDef = newDefault.getNodeDef();
                if (nodeDef != null)
                {
                    REQUIRE(nodeDef.getVersionString() == VERSION2);
                }
            }

            List<NodeDef> matchingNodeDefs = new List<NodeDef>();
            foreach (NodeDef docNodeDef in doc.getNodeDefs())
            {
                if (docNodeDef.getNodeString() == NODENAME)
                {
                    matchingNodeDefs.Add(docNodeDef);
                }
            }
            bool findDefault = false;
            foreach (NodeDef matchingDef in matchingNodeDefs)
            {
                if (matchingDef.getDefaultVersion())
                {
                    findDefault = true;
                    REQUIRE(matchingDef.getVersionString() == VERSION2);
                    break;
                }
            }
            REQUIRE(findDefault);

            doc.removeChild(graph.getName());
        }

        REQUIRE(doc.validate());
    }

    [TEST("Core", "SetNameGlobal")]
    public static void Core_SetNameGlobal()
    {
        TEST_CASE(() =>
        {
            Document doc = createDocument();

            string type = "float";
            string new_name = "new_name";
            SECTION("node", () =>
            {
                // Upstream -> Downstream
                SECTION("Node within NodeGraph -> NodeGraph output", () =>
                {
                    NodeGraph parentGraph = doc.addNodeGraph();

                    Node upstreamNode = parentGraph.addNode("constant", "upstreamNode", type);
                    upstreamNode.addOutput("output", type);

                    PortElement downstreamOutput = parentGraph.addOutput("out", type);
                    downstreamOutput.setNodeName(upstreamNode.getName());

                    upstreamNode.setNameGlobal(new_name);
                    REQUIRE(upstreamNode.getName() == new_name);
                    REQUIRE(downstreamOutput.getNodeName() == new_name);
                });
                SECTION("Free node", () =>
                {
                    SECTION("Free Node -> Free Node", () =>
                    {
                        Node upstreamNode = doc.addNode("constant", "upstreamNode", type);
                        upstreamNode.addOutput("output", type);

                        Node downStreamNode = doc.addNode("downStreamNode");
                        Input downstreamInput = downStreamNode.addInput("in", type);
                        downstreamInput.setNodeName(upstreamNode.getName());
                        SECTION("Update references", () =>
                        {
                            upstreamNode.setNameGlobal(new_name);
                            REQUIRE(upstreamNode.getName() == new_name);
                            REQUIRE(downstreamInput.getNodeName() == new_name);
                        });
                    });
                    SECTION("Free Node -> NodeGraph input", () =>
                    {
                        Node upstreamNode = doc.addNode("constant", "upstreamNode", type);
                        upstreamNode.addOutput("output", type);

                        NodeGraph downstreamGraph = doc.addNodeGraph();
                        Input downstreamInput = downstreamGraph.addInput("input", type);
                        downstreamInput.setNodeName(upstreamNode.getName());

                        upstreamNode.setNameGlobal(new_name);
                        REQUIRE(upstreamNode.getName() == new_name);
                        REQUIRE(downstreamInput.getNodeName() == new_name);
                    });
                });
                SECTION("Node -> NodeDef output", () =>
                {
                    NodeGraph compoundNodeGraph = doc.addNodeGraph();
                    Output compoundNodeGraphOutput = compoundNodeGraph.addOutput("output", type);

                    Node compoundNodeGraphNode = compoundNodeGraph.addNode("constant", "upstreamNode", type);
                    compoundNodeGraphNode.addOutput("output", type);
                    compoundNodeGraphOutput.setNodeName(compoundNodeGraphNode.getName());

                    string newNodeDefName = doc.createValidChildName("ND_" + compoundNodeGraph.getName());
                    string newGraphName = doc.createValidChildName("NG_" + compoundNodeGraph.getName());
                    NodeDef nodeDef = doc.addNodeDefFromGraph(compoundNodeGraph, newNodeDefName, "NODENAME", newGraphName);
                    NodeGraph functionalNodeGraph = nodeDef.getImplementation().asA<NodeGraph>();

                    Node upstreamNode = functionalNodeGraph.getChild(compoundNodeGraphNode.getName()).asA<Node>();
                    REQUIRE(upstreamNode != null);

                    Output downstreamOutput = functionalNodeGraph.getOutput(compoundNodeGraphOutput.getName());
                    REQUIRE(downstreamOutput != null);

                    upstreamNode.setNameGlobal(new_name);
                    REQUIRE(upstreamNode.getName() == new_name);
                    REQUIRE(downstreamOutput.getNodeName() == new_name);
                });
            });
            SECTION("nodegraph", () =>
            {
                NodeGraph upstreamGraph = doc.addNodeGraph();
                Node upstreamGraphNode = upstreamGraph.addNode("constant", "upstreamNode", type);
                Output upstreamGraphOutput = upstreamGraph.addOutput("output", type);
                upstreamGraphOutput.setNodeName(upstreamGraphNode.getName());

                SECTION("Node Graph -> Node", () =>
                {
                    Node downstreamNode = doc.addNode("constant", "downstreamNode", type);
                    Input downstreamInput = downstreamNode.addInput("input", type);
                    downstreamInput.setNodeGraphString(upstreamGraph.getName());
                    downstreamInput.setOutputString(upstreamGraphOutput.getName());

                    upstreamGraph.setNameGlobal(new_name);
                    REQUIRE(upstreamGraph.getName() == new_name);
                    REQUIRE(downstreamInput.getNodeGraphString() == new_name);
                });
                SECTION("Node Graph -> Node Graph", () =>
                {
                    NodeGraph downstreamGraph = doc.addNodeGraph();
                    Input downstreamInput = downstreamGraph.addInput("input", type);
                    downstreamInput.setNodeGraphString(upstreamGraph.getName());
                    downstreamInput.setOutputString(upstreamGraphOutput.getName());

                    upstreamGraph.setNameGlobal(new_name);
                    REQUIRE(upstreamGraph.getName() == new_name);
                    REQUIRE(downstreamInput.getNodeGraphString() == new_name);
                });
            });
        });
    }
}
