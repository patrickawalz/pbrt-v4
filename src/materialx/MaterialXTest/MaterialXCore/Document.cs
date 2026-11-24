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

public static class CoreDocumentTests
{
    [TEST("Core", "Document")]
    public static void Core_Document()
    {
        // Create a document.
        Document doc = createDocument();

        // Test version strings.
        REQUIRE(stringStartsWith(getVersionString(), doc.getVersionString()));

        // Test version integers.
        REQUIRE(doc.getVersionIntegers().Item1 == getVersionIntegers().Item1);
        REQUIRE(doc.getVersionIntegers().Item2 == getVersionIntegers().Item2);

        // Create a node graph with a constant color output.
        NodeGraph nodeGraph = doc.addNodeGraph();
        Node constant = nodeGraph.addNode("constant");
        constant.setInputValue("value", new Color3(0.5f));
        Output output = nodeGraph.addOutput();
        output.setConnectedNode(constant);
        REQUIRE(output.isColorType());
        REQUIRE(doc.validate());

        // Create and test a type mismatch in a connection.
        output.setType("float");
        REQUIRE(!doc.validate());
        output.setType("color3");
        REQUIRE(doc.validate());

        // Test hierarchical name paths.
        REQUIRE(constant.getNamePath() == "nodegraph1/node1");
        REQUIRE(constant.getNamePath(nodeGraph) == "node1");

        // Test getting elements by path
        REQUIRE(doc.getDescendant("") == doc);
        REQUIRE(doc.getDescendant("nodegraph1") == nodeGraph);
        REQUIRE(doc.getDescendant("nodegraph1/node1") == constant);
        REQUIRE(doc.getDescendant("missingElement") == null);
        REQUIRE(doc.getDescendant("nodegraph1/missingNode") == null);
        REQUIRE(nodeGraph.getDescendant("") == nodeGraph);
        REQUIRE(nodeGraph.getDescendant("node1") == constant);
        REQUIRE(nodeGraph.getDescendant("missingNode") == null);

        // Create a simple shader interface.
        NodeDef simpleSrf = doc.addNodeDef("", SURFACE_SHADER_TYPE_STRING, "simpleSrf");
        simpleSrf.setInputValue("diffColor", new Color3(1.0f));
        simpleSrf.setInputValue("specColor", new Color3(0.0f));
        Input roughness = simpleSrf.setInputValue("roughness", 0.25f);
        REQUIRE(!roughness.getIsUniform());
        roughness.setIsUniform(true);
        REQUIRE(roughness.getIsUniform());

        // Instantiate shader and material nodes.
        Node shaderNode = doc.addNodeInstance(simpleSrf);
        Node materialNode = doc.addMaterialNode("", shaderNode);
        REQUIRE(materialNode.getUpstreamElement() == shaderNode);

        // Bind the diffuse color input to the constant color output.
        shaderNode.setConnectedOutput("diffColor", output);
        REQUIRE(shaderNode.getUpstreamElement() == constant);

        // Bind the roughness input to a value.
        Input instanceRoughness = shaderNode.setInputValue("roughness", 0.5f);
        REQUIRE(instanceRoughness.getValue().asA<float>() == 0.5f);
        REQUIRE(instanceRoughness.getDefaultValue().asA<float>() == 0.25f);

        // Create a collection 
        Collection collection = doc.addCollection();
        REQUIRE(doc.getCollections().Count == 1);
        REQUIRE(doc.getCollection(collection.getName()) != null);
        doc.removeCollection(collection.getName());
        REQUIRE(doc.getCollections().Count == 0);

        // Create a property set
        PropertySet propertySet = doc.addPropertySet();
        REQUIRE(doc.getPropertySets().Count == 1);
        REQUIRE(doc.getPropertySet(propertySet.getName()) != null);
        doc.removePropertySet(propertySet.getName());
        REQUIRE(doc.getPropertySets().Count == 0);

        // Validate the document.
        REQUIRE(doc.validate());

        // Create a namespaced custom library.
        Document customLibrary = createDocument();
        customLibrary.setNamespace("custom");
        NodeGraph customNodeGraph = customLibrary.addNodeGraph("NG_custom");
        NodeDef customNodeDef = customLibrary.addNodeDef("ND_simpleSrf", SURFACE_SHADER_TYPE_STRING, "simpleSrf");
        Implementation customImpl = customLibrary.addImplementation("IM_custom");
        customNodeGraph.addNodeInstance(customNodeDef, "custom1");
        customImpl.setNodeDef(customNodeDef);
        REQUIRE(customLibrary.validate());

        // Import the custom library.
        Document customDatalibrary = createDocument();
        customDatalibrary.importLibrary(customLibrary);

        // Set data library
        doc.setDataLibrary(customDatalibrary);
        NodeGraph importedNodeGraph = doc.getNodeGraph("custom:NG_custom");
        NodeDef importedNodeDef = doc.getNodeDef("custom:ND_simpleSrf");
        Implementation importedImpl = doc.getImplementation("custom:IM_custom");
        Node importedNode = importedNodeGraph.getNode("custom1");
        REQUIRE(importedNodeDef != null);
        REQUIRE(importedNode.getNodeDef() == importedNodeDef);
        REQUIRE(importedImpl.getNodeDef() == importedNodeDef);

        // Validate the combined document.
        REQUIRE(doc.validate());
    }

    [TEST("Core", "DocumentEquivalence")]
    public static void Core_DocumentEquivalence()
    {
        Document doc = createDocument();
        List<(string, string)> inputMap = new List<(string, string)>();

        inputMap.Add(("color3", "  1.0,   +2.0,  3.0   "));
        inputMap.Add(("color4", "1.0,   2.00, 0.3000, -4"));
        inputMap.Add(("integer", "  12 "));
        inputMap.Add(("matrix33",
                      "01.0,         2.0,  0000.2310, " +
                      "   01.0,         2.0,  0000.2310, " +
                      "01.0,         2.0,  0000.2310       "));
        inputMap.Add(("matrix44",
                      "01.0,         2.0,  0000.2310, 0.100, " +
                      "01.0,         2.0,  0000.2310, 0.100, " +
                      "01.0,         2.0,  0000.2310, 0.100, " +
                      "01.0,         2.0,  0000.2310, 0.100"));
        inputMap.Add(("vector2", "1.0,   0.012345608")); // For precision check
        inputMap.Add(("vector3", "  1.0,   +2.0,  3.0   "));
        inputMap.Add(("vector4", "1.0,   2.00, 0.3000, -4"));
        inputMap.Add(("string", "mystring"));
        inputMap.Add(("boolean", "false"));
        inputMap.Add(("filename", "filename1"));
        inputMap.Add(("float", "  1.2e-10  "));
        inputMap.Add(("float", "  00.1000  "));

        int index = 0;
        Element child = doc.addNodeGraph("mygraph");
        NodeGraph graph = child.asA<NodeGraph>();
        // Add comment block at the start of the first doc to check skipping
        Element comment = doc.addChildOfCategory(CommentElement.CATEGORY);
        comment.setDocString("Comment 1");
        foreach ((string, string) it in inputMap)
        {
            string inputType = (it).Item1;
            Input input = graph.addInput("input_" + Mx.to_string(index), inputType);
            if (inputType == "float")
            {
                input.setAttribute(ValueElement.UI_MIN_ATTRIBUTE, "  0.0100 ");
                input.setAttribute(ValueElement.UI_MAX_ATTRIBUTE, "  01.0100 ");
                index++;
            }
            else
            {
                input.setName("input_" + inputType); // Set by name for difference in order test
            }
            input.setValueString((it).Item2);
        }

        Document doc2 = createDocument();
        List<(string, string)> inputMap2 = new List<(string, string)>();
        inputMap2.Add(("color4", "1, 2, 0.3, -4")); 
        inputMap2.Add(("integer", "12"));
        inputMap2.Add(("matrix33", "1, 2, 0.231,  1, 2, 0.231,  1, 2, 0.231,  1, 2, 0.231"));
        inputMap2.Add(("matrix44", "1, 2, 0.231, 0.1, 1, 2, 0.231, 0.1, 1, 2, 0.231, 0.1, 1, 2, 0.231, 0.1"));
        inputMap2.Add(("vector2", "1, 0.012345611")); // For precision check
        inputMap2.Add(("string", "mystring"));
        inputMap2.Add(("boolean", "false"));
        inputMap2.Add(("color3", "1, 2, 3"));
        inputMap2.Add(("vector3", "1, 2, 3"));
        inputMap2.Add(("vector4", "1, 2, 0.3, -4"));
        inputMap2.Add(("filename", "filename1"));
        inputMap2.Add(("float", "1.2e-10"));
        inputMap2.Add(("float", "0.1"));

        index = 0;
        child = doc2.addNodeGraph("mygraph");
        graph = child.asA<NodeGraph>();
        List<Input> floatInputs = new List<Input>();
        foreach ((string, string) it in inputMap2)
        {
            string inputType = (it).Item1;
            Input input = graph.addInput("input_" + Mx.to_string(index), inputType);
            // Note: order of value and ui attributes is different for ordering comparison
            input.setValueString((it).Item2);
            if (inputType == "float")
            {
                input.setAttribute(ValueElement.UI_MIN_ATTRIBUTE, "  0.01");
                input.setAttribute(ValueElement.UI_MAX_ATTRIBUTE, "  1.01");
                floatInputs.Add(input);
                index++;
            }
            else
            {
                input.setName("input_" + inputType);
            }
        }
        // Add comment blocks at end of second doc to check value and count checks
        comment = doc2.addChildOfCategory(CommentElement.CATEGORY);
        comment.setDocString("Comment 2");
        comment = doc2.addChildOfCategory(CommentElement.CATEGORY);
        comment.setDocString("Comment 3");

        ElementEquivalenceOptions options = new ElementEquivalenceOptions();
        StringBuilder message = new StringBuilder();

        // Check that this fails when not performing value comparisons
        options.performValueComparisons = false;
        bool equivalent = doc.isEquivalent(doc2, options, message);
        REQUIRE(!equivalent);

        // Check attribute values 
        options.performValueComparisons = true;
        equivalent = doc.isEquivalent(doc2, options, message);
        REQUIRE(equivalent);

        int currentPrecision = Value.getFloatPrecision();
        // This will compare 0.012345608 versus: 1, 0.012345611 for input10
        options.floatPrecision = 8;
        equivalent = doc.isEquivalent(doc2, options);
        REQUIRE(!equivalent);
        options.floatPrecision = currentPrecision;

        // Check attribute filtering of inputs
        options.attributeExclusionList = new StringSet { ValueElement.UI_MIN_ATTRIBUTE, ValueElement.UI_MAX_ATTRIBUTE };
        foreach (Input floatInput in floatInputs)
        {
            floatInput.setAttribute(ValueElement.UI_MIN_ATTRIBUTE, "0.9");
            floatInput.setAttribute(ValueElement.UI_MAX_ATTRIBUTE, "100.0");
        }
        equivalent = doc.isEquivalent(doc2, options, message);
        REQUIRE(equivalent);
        foreach (Input floatInput in floatInputs)
        {
            floatInput.setAttribute(ValueElement.UI_MIN_ATTRIBUTE, "  0.01");
            floatInput.setAttribute(ValueElement.UI_MAX_ATTRIBUTE, "  1.01");
        }

        // Check for child name mismatch
        Element mismatchElement = doc.getDescendant("mygraph/input_color4");
        string previousName = mismatchElement.getName();
        mismatchElement.setName("mismatch_color4");
        equivalent = doc.isEquivalent(doc2, options, message);
        REQUIRE(!equivalent);
        mismatchElement.setName(previousName);
        equivalent = doc.isEquivalent(doc2, options, message);
        REQUIRE(equivalent);

        // Check for functional nodegraphs
        NodeGraph nodeGraph = doc.getNodeGraph("mygraph");
        REQUIRE(nodeGraph != null);
        doc.addNodeDef("ND_mygraph");
        nodeGraph.setNodeDefString("ND_mygraph");
        NodeGraph nodeGraph2 = doc2.getNodeGraph("mygraph");
        REQUIRE(nodeGraph2 != null);
        doc2.addNodeDef("ND_mygraph");
        nodeGraph2.setNodeDefString("ND_mygraph");
        equivalent = doc.isEquivalent(doc2, options, message);
        REQUIRE(!equivalent);
    }

    [TEST("Core", "DocumentCreateValidChildName")]
    public static void Core_DocumentCreateValidChildName()
    {
        // Create a custom library.
        Document customLibrary = createDocument();
        NodeDef customNodeDef = customLibrary.addNodeDef("ND_simpleSrf", SURFACE_SHADER_TYPE_STRING, "simpleSrf");
        Implementation customImpl = customLibrary.addImplementation("IM_custom");
        customImpl.setNodeDef(customNodeDef);
        REQUIRE(customLibrary.validate());

        Document doc = null;

        // Create a document and import the data library
        doc = createDocument();
        doc.importLibrary(customLibrary);

        // test createValidChildName with imported data library
        {
            // add a non-clashing name
            string nodename_01_orig = "non_clashing_name";
            string nodename_01 = doc.createValidChildName(nodename_01_orig);
            REQUIRE(nodename_01_orig == nodename_01);

            Node node_01 = doc.addNodeInstance(customNodeDef, nodename_01);
            REQUIRE(doc.validate());
            REQUIRE(node_01.getName() == nodename_01);

            // add a clashing name - which needs to be uniquified.
            string nodename_02_orig = "non_clashing_name";
            string nodename_02 = doc.createValidChildName(nodename_02_orig);
            REQUIRE(nodename_02_orig != nodename_02);

            // add the same name this should be uniquified
            Node node_02 = doc.addNodeInstance(customNodeDef, nodename_02);
            REQUIRE(doc.validate());
            REQUIRE(node_02.getName() == nodename_02);

            // add a name that clashes with something that is in the data library
            string nodename_03_orig = "ND_simpleSrf";
            string nodename_03 = doc.createValidChildName(nodename_03_orig);
            REQUIRE(nodename_03_orig != nodename_03);

            Node node_03 = doc.addNodeInstance(customNodeDef, nodename_03);
            REQUIRE(doc.validate());
            REQUIRE(node_03.getName() == nodename_03);

            // an empty name should be replaced with a valid placeholder
            REQUIRE(doc.createValidChildName("").Length != 0);
        }

        // Create a document and reference the data library
        doc = createDocument();
        doc.setDataLibrary(customLibrary);

        // test createValidChildName with referenced data library
        {
            // add a non-clashing name
            string nodename_01_orig = "non_clashing_name";
            string nodename_01 = doc.createValidChildName(nodename_01_orig);
            REQUIRE(nodename_01_orig == nodename_01);

            Node node_01 = doc.addNodeInstance(customNodeDef, nodename_01);
            REQUIRE(doc.validate());
            REQUIRE(node_01.getName() == nodename_01);

            // add a clashing name - which needs to be uniquified.
            string nodename_02_orig = "non_clashing_name";
            string nodename_02 = doc.createValidChildName(nodename_02_orig);
            REQUIRE(nodename_02_orig != nodename_02);

            // add the same name this should be uniquified
            Node node_02 = doc.addNodeInstance(customNodeDef, nodename_02);
            REQUIRE(doc.validate());
            REQUIRE(node_02.getName() == nodename_02);

            // add a name that clashes with something that is in the data library
            string nodename_03_orig = "ND_simpleSrf";
            string nodename_03 = doc.createValidChildName(nodename_03_orig);
            REQUIRE(nodename_03_orig != nodename_03);

            Node node_03 = doc.addNodeInstance(customNodeDef, nodename_03);
            REQUIRE(doc.validate());
            REQUIRE(node_03.getName() == nodename_03);

            // an empty name should be replaced with a valid placeholder
            REQUIRE(doc.createValidChildName("").Length != 0);
        }
    }

    [TEST("Core", "DocumentUpgradeSwitchNullValue")]
    public static void Core_DocumentUpgradeSwitchNullValue()
    {
        // https://github.com/AcademySoftwareFoundation/MaterialX/issues/3067
        // A "which" value that fails to parse as its declared type (e.g. a
        // non-numeric string on an integer input) makes getValue() return
        // null; the 1.38-1.39 switch upgrade must not dereference it.
        Document doc = createDocument();
        XmlReadOptions readOptions = new XmlReadOptions();
        readOptions.upgradeVersion = false;
        readFromXmlString(
            doc,
            @"<?xml version=""1.0""?>
<materialx version=""1.38"">
  <nodegraph name=""NG1"">
    <switch name=""sw1"" type=""integer"" nodedef=""ND_switch"">
      <input name=""which"" type=""integer"" value=""abc""/>
    </switch>
  </nodegraph>
</materialx>
",
            new FileSearchPath(),
            readOptions);
        REQUIRE_NOTHROW(() => { doc.upgradeVersion(); });

        // An unparseable "which" value is left untouched by the upgrade, rather
        // than being silently cleared or otherwise modified.
        Node node = doc.getNodeGraph("NG1").getNode("sw1");
        REQUIRE(node.getInput("which").getValueString() == "abc");
    }

    [TEST("Core", "DocumentUpgradeSwizzleEmptyValue")]
    public static void Core_DocumentUpgradeSwizzleEmptyValue()
    {
        // https://github.com/AcademySoftwareFoundation/MaterialX/issues/3068
        // A swizzle "in" input with an empty value ("") makes the 1.38-1.39
        // upgrade's origValueTokens empty; a missing/invalid channel name
        // must not index into it out of range.
        Document doc = createDocument();
        XmlReadOptions readOptions = new XmlReadOptions();
        readOptions.upgradeVersion = false;
        readFromXmlString(
            doc,
            @"<?xml version=""1.0""?>
<materialx version=""1.38"">
  <nodegraph name=""NG1"">
    <swizzle name=""swz1"" type=""color3"" nodedef=""ND_swizzle"">
      <input name=""in"" type=""color3"" value=""""/>
      <input name=""channels"" type=""string"" value=""""/>
    </swizzle>
  </nodegraph>
</materialx>
",
            new FileSearchPath(),
            readOptions);
        REQUIRE_NOTHROW(() => { doc.upgradeVersion(); });

        // The empty "in" value and missing/invalid channel name fall back to "0",
        // matching this function's existing convention elsewhere for a
        // missing/unparseable value.
        Node node = doc.getNodeGraph("NG1").getNode("swz1");
        REQUIRE(node.getCategory() == "constant");
        REQUIRE(node.getInput("value").getValueString() == "0, 0, 0");
    }
}
