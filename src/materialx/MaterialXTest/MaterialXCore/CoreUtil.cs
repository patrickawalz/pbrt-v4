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

public static class CoreCoreUtilTests
{
    [TEST("Core", "VersionComparison")]
    public static void Core_VersionComparison()
    {
        // Test for version comparison (C++ context)
        REQUIRE(MATERIALX_VERSION_INDEX > MATERIALX_GENERATE_INDEX(1, 38, 8));

        // The preprocessor comparison of the version has no counterpart here.
    }

    [TEST("Core", "StringUtilities")]
    public static void Core_StringUtilities()
    {
        string invalidName = "test.name";
        REQUIRE(isValidName(invalidName) == false);
        REQUIRE(isValidName(createValidName(invalidName)) == true);

        REQUIRE(createValidName("test.name.1") == "test_name_1");
        REQUIRE(createValidName("test*name>2") == "test_name_2");
        REQUIRE(createValidName("testName...") == "testName___");

        REQUIRE(incrementName("testName") == "testName2");
        REQUIRE(incrementName("testName0") == "testName1");
        REQUIRE(incrementName("testName99") == "testName100");
        REQUIRE(incrementName("1testName1") == "1testName2");
        REQUIRE(incrementName("41") == "42");

        REQUIRE(vectorEqual(splitString("robot1, robot2", ", "), new List<string> { "robot1", "robot2" }));
        REQUIRE(vectorEqual(splitString("[one...two...three]", "[.]"), new List<string> { "one", "two", "three" }));

        REQUIRE(stringToLower("testName") == "testname");
        REQUIRE(stringToLower("testName1") == "testname1");

        REQUIRE(stringStartsWith("testName", "test"));
        REQUIRE(!stringStartsWith("testName", "Name"));
        REQUIRE(stringEndsWith("testName", "Name"));
        REQUIRE(!stringEndsWith("testName", "test"));
    }

    [TEST("Core", "PrintUtilities")]
    public static void Core_PrintUtilities()
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

        string blessed =
            "digraph {\n" +
            "    \"image\" [shape=box];\n" +
            "    \"image2\" [shape=box];\n" +
            "    \"constant\" [shape=box];\n" +
            "    \"constant2\" [shape=box];\n" +
            "    \"noise3d\" [shape=box];\n" +
            "    \"add\" [shape=box];\n" +
            "    \"add2\" [shape=box];\n" +
            "    \"multiply\" [shape=box];\n" +
            "    \"add3\" [shape=box];\n" +
            "    \"mix\" [shape=box];\n" +
            "    \"mix\" -> \"output\" [label=\"\"];\n" +
            "    \"multiply\" -> \"mix\" [label=\"fg\"];\n" +
            "    \"image\" -> \"multiply\" [label=\"in1\"];\n" +
            "    \"add\" -> \"multiply\" [label=\"in2\"];\n" +
            "    \"constant\" -> \"add\" [label=\"in1\"];\n" +
            "    \"constant2\" -> \"add\" [label=\"in2\"];\n" +
            "    \"add3\" -> \"mix\" [label=\"bg\"];\n" +
            "    \"add\" -> \"add3\" [label=\"in1\"];\n" +
            "    \"add2\" -> \"add3\" [label=\"in2\"];\n" +
            "    \"constant2\" -> \"add2\" [label=\"in1\"];\n" +
            "    \"image2\" -> \"add2\" [label=\"in2\"];\n" +
            "    \"noise3d\" -> \"mix\" [label=\"mask\"];\n" +
            "}\n";

        REQUIRE(nodeGraph.asStringDot() == blessed);
    }
}
