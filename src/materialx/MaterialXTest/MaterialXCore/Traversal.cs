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

public static class CoreTraversalTests
{
    [TEST("Core", "IntraGraphTraversal")]
    public static void Core_IntraGraphTraversal()
    {
        // Test null iterators.
        TreeIterator nullTree = getNullTreeIterator();
        GraphIterator nullGraph = getNullGraphIterator();
        REQUIRE(nullTree.getElement() == null);
        REQUIRE(nullGraph.getEdge() == getNullEdge());
        nullTree.next();
        nullGraph.next();
        REQUIRE(nullTree.isEqual(getNullTreeIterator()));
        REQUIRE(nullGraph.isEqual(getNullGraphIterator()));

        // Create a document.
        Document doc = createDocument();

        // Create a node graph with the following structure:
        //
        // [image1] [constant]     [image2]
        //        \ /                 |   
        //    [multiply]          [contrast]         [noise3d]
        //             \____________  |  ____________/
        //                          [mix]
        //                            |
        //                         [output]
        //
        NodeGraph nodeGraph = doc.addNodeGraph();
        Node image1 = nodeGraph.addNode("image");
        Node image2 = nodeGraph.addNode("image");
        Node constant = nodeGraph.addNode("constant");
        Node multiply = nodeGraph.addNode("multiply");
        Node contrast = nodeGraph.addNode("contrast");
        Node noise3d = nodeGraph.addNode("noise3d");
        Node mix = nodeGraph.addNode("mix");
        Output output = nodeGraph.addOutput();
        multiply.setConnectedNode("in1", image1);
        multiply.setConnectedNode("in2", constant);
        contrast.setConnectedNode("in", image2);
        mix.setConnectedNode("fg", multiply);
        mix.setConnectedNode("bg", contrast);
        mix.setConnectedNode("mask", noise3d);
        output.setConnectedNode(mix);

        // Validate the document.
        REQUIRE(doc.validate());

        // Traverse the document tree (implicit iterator).
        int nodeCount = 0;
        foreach (Element elem in doc.traverseTree())
        {
            REQUIRE(elem.getName() == createValidName(elem.getName()));
            if (elem.isA<Node>())
            {
                nodeCount++;
            }
        }
        REQUIRE(nodeCount == 7);

        // Traverse the document tree (explicit iterator).
        nodeCount = 0;
        int maxElementDepth = 0;
        for (TreeIterator it = doc.traverseTree().begin(); !it.isEqual(TreeIterator.end()); it.next())
        {
            Element elem = it.getElement();
            if (elem.isA<Node>())
            {
                nodeCount++;
            }
            maxElementDepth = Math.Max(maxElementDepth, it.getElementDepth());
        }
        REQUIRE(nodeCount == 7);
        REQUIRE(maxElementDepth == 3);

        // Traverse the document tree (prune subtree).
        nodeCount = 0;
        for (TreeIterator it = doc.traverseTree().begin(); !it.isEqual(TreeIterator.end()); it.next())
        {
            Element elem = it.getElement();
            if (elem.isA<Node>())
            {
                nodeCount++;
            }
            if (elem.isA<NodeGraph>())
            {
                it.setPruneSubtree(true);
            }
        }
        REQUIRE(nodeCount == 0);

        // Traverse upstream from the graph output (implicit iterator).
        nodeCount = 0;
        foreach (Edge edge in output.traverseGraph())
        {
            Element upstreamElem = edge.getUpstreamElement();
            Element connectingElem = edge.getConnectingElement();
            Element downstreamElem = edge.getDownstreamElement();
            if (upstreamElem.isA<Node>())
            {
                nodeCount++;
                if (downstreamElem.isA<Node>())
                {
                    REQUIRE(connectingElem.isA<Input>());
                }
            }
        }
        REQUIRE(nodeCount == 7);

        // Traverse upstream from the graph output (explicit iterator).
        nodeCount = 0;
        maxElementDepth = 0;
        int maxNodeDepth = 0;
        for (GraphIterator it = output.traverseGraph().begin(); !it.isEnd(); it.next())
        {
            Element upstreamElem = it.getUpstreamElement();
            Element connectingElem = it.getConnectingElement();
            Element downstreamElem = it.getDownstreamElement();
            if (upstreamElem.isA<Node>())
            {
                nodeCount++;
                if (downstreamElem.isA<Node>())
                {
                    REQUIRE(connectingElem.isA<Input>());
                }
            }
            maxElementDepth = Math.Max(maxElementDepth, it.getElementDepth());
            maxNodeDepth = Math.Max(maxNodeDepth, it.getNodeDepth());
        }
        REQUIRE(nodeCount == 7);
        REQUIRE(maxElementDepth == 3);
        REQUIRE(maxNodeDepth == 3);

        // Traverse upstream from the graph output (prune subgraph).
        nodeCount = 0;
        for (GraphIterator it = output.traverseGraph().begin(); !it.isEnd(); it.next())
        {
            Element upstreamElem = it.getUpstreamElement();
            if (upstreamElem.isA<Node>())
            {
                nodeCount++;
                if (upstreamElem.getCategory() == "multiply")
                {
                    it.setPruneSubgraph(true);
                }
            }
        }
        REQUIRE(nodeCount == 5);

        // Create and detect a cycle.
        multiply.setConnectedNode("in2", mix);
        REQUIRE(output.hasUpstreamCycle());
        REQUIRE(!doc.validate());
        multiply.setConnectedNode("in2", constant);
        REQUIRE(!output.hasUpstreamCycle());
        REQUIRE(doc.validate());

        // Create and detect a loop.
        contrast.setConnectedNode("in", contrast);
        REQUIRE(output.hasUpstreamCycle());
        REQUIRE(!doc.validate());
        contrast.setConnectedNode("in", image2);
        REQUIRE(!output.hasUpstreamCycle());
        REQUIRE(doc.validate());
    }

    [TEST("Core", "InterGraphTraversal")]
    public static void Core_InterGraphTraversal()
    {
        FileSearchPath searchPath = getDefaultDataSearchPath();
        Document doc = createDocument();
        readFromXmlFile(doc, "resources/Materials/TestSuite/stdlib/nodegraphs/nodegraph_nodegraph.mtlx", searchPath);

        foreach (NodeGraph graph in doc.getNodeGraphs())
        {
            foreach (Input interfaceInput in graph.getInputs())
            {
                if (interfaceInput.getNodeName().Length != 0 || interfaceInput.getNodeGraphString().Length != 0)
                {
                    REQUIRE(interfaceInput.getConnectedNode() != null);                    
                }
            }
        }
    }
}
