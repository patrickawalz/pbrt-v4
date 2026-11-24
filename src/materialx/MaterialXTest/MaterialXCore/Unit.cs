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

public static class CoreUnitTests
{
    private const float EPSILON = 1e-4f;

    [TEST("Core", "UnitAttribute")]
    public static void Core_UnitAttribute()
    {
        FileSearchPath searchPath = getDefaultDataSearchPath();
        Document doc = createDocument();
        loadLibraries(new List<FilePath> { new FilePath("libraries") }, searchPath, doc);

        List<UnitTypeDef> unitTypeDefs = doc.getUnitTypeDefs();
        REQUIRE(unitTypeDefs.Count != 0);

        NodeGraph nodeGraph = doc.addNodeGraph();
        nodeGraph.setName("graph1");

        // Basic get/set unit testing
        Node constant = nodeGraph.addNode("constant");
        constant.setName("constant1");
        constant.setInputValue("value", new Color3(0.5f));
        Input input = constant.getInput("value");
        input.setUnitType("distance");
        input.setUnit("meter");
        REQUIRE(input.hasUnit());
        REQUIRE(input.getUnit().Length != 0);

        // Test for valid unit names
        Output output = nodeGraph.addOutput();
        output.setConnectedNode(constant);
        output.setUnitType("distance");
        output.setUnit("bad unit");
        REQUIRE(!output.validate());
        output.setUnit("foot");
        REQUIRE(output.hasUnit());
        REQUIRE(output.getUnit().Length != 0);

        REQUIRE(doc.validate());

        // Test for target unit specified on a nodedef
        NodeDef customNodeDef = doc.addNodeDef("ND_dummy", "float", "dummy");
        input = customNodeDef.setInputValue("angle", 23.0f, "float");
        input.setUnit("degree");
        Node custom = doc.addNodeInstance(customNodeDef);
        input = custom.setInputValue("angle", 45.0f, "float");
        input.setUnit("radian");
        REQUIRE(input.getUnit() == "radian");
        REQUIRE(input.getActiveUnit() == "degree");
    }

    [TEST("Core", "UnitEvaluation")]
    public static void Core_UnitEvaluation()
    {
        FileSearchPath searchPath = getDefaultDataSearchPath();
        Document doc = createDocument();
        loadLibraries(new List<FilePath> { new FilePath("libraries") }, searchPath, doc);

        //
        // Test distance converter
        //
        UnitTypeDef distanceTypeDef = doc.getUnitTypeDef("distance");
        REQUIRE(distanceTypeDef != null);

        UnitConverterRegistry registry = UnitConverterRegistry.create();
        UnitConverterRegistry registry2 = UnitConverterRegistry.create();
        REQUIRE(registry == registry2);

        LinearUnitConverter converter = LinearUnitConverter.create(distanceTypeDef);
        REQUIRE(converter != null);
        registry.addUnitConverter(distanceTypeDef, converter);
        UnitConverter uconverter = registry.getUnitConverter(distanceTypeDef);
        REQUIRE(uconverter != null);

        // Use converter to convert
        float result = converter.convert(0.1f, "kilometer", "millimeter");
        REQUIRE((result - 100000.0f) < EPSILON);
        result = converter.convert(2.3f, "meter", "meter");
        REQUIRE((result - 2.3f) < EPSILON);
        result = converter.convert(1.0f, "mile", "meter");
        REQUIRE((result - 1609.344f) < EPSILON);
        result = converter.convert(1.0f, "meter", "mile");
        REQUIRE((result - (1.0 / 0.000621f)) < EPSILON);

        // Use explicit converter values
        Dictionary<string, float> unitScale = converter.getUnitScale();
        result = 0.1f * unitScale["kilometer"] / unitScale["millimeter"];
        REQUIRE((result - 100000.0f) < EPSILON);

        // Test integrer mapping
        int unitNumber = converter.getUnitAsInteger("mile");
        string unitName = converter.getUnitFromInteger(unitNumber);
        REQUIRE(unitName == "mile");

        //
        // Add angle converter
        //
        UnitTypeDef angleTypeDef = doc.getUnitTypeDef("angle");
        REQUIRE(angleTypeDef != null);
        LinearUnitConverter converter2 = LinearUnitConverter.create(angleTypeDef);
        REQUIRE(converter2 != null);
        registry.addUnitConverter(angleTypeDef, converter2);
        UnitConverter uconverter2 = registry.getUnitConverter(angleTypeDef);
        REQUIRE(uconverter2 != null);
        result = converter2.convert(2.5f, "degree", "degree");
        REQUIRE((result - 2.5f) < EPSILON);
        result = converter2.convert(2.0f, "radian", "degree");
        REQUIRE((result - 114.591559026f) < EPSILON);
    }

    [TEST("Core", "UnitDocument")]
    public static void Core_UnitDocument()
    {
        FileSearchPath searchPath = getDefaultDataSearchPath();
        Document stdlib = createDocument();
        loadLibraries(new List<FilePath> { new FilePath("libraries") }, searchPath, stdlib);
        FilePath examplesPath = searchPath.find("resources/Materials/TestSuite/stdlib/units");
        searchPath.append(examplesPath);

        string DISTANCE_DEFAULT = "meter";
        // Read and validate each example document.
        foreach (string filename in examplesPath.getFilesInDirectory(MTLX_EXTENSION))
        {
            Document doc = createDocument();
            readFromXmlFile(doc, filename, searchPath);
            doc.importLibrary(stdlib);

            UnitTypeDef distanceTypeDef = doc.getUnitTypeDef("distance");
            REQUIRE(distanceTypeDef != null);

            UnitConverter uconverter = LinearUnitConverter.create(distanceTypeDef);
            REQUIRE(uconverter != null);
            UnitConverterRegistry registry = UnitConverterRegistry.create();
            registry.addUnitConverter(distanceTypeDef, uconverter);
            uconverter = registry.getUnitConverter(distanceTypeDef);
            REQUIRE(uconverter != null);

            // Traverse the document tree
            foreach (Element elem in doc.traverseTree())
            {
                // If we have nodes with inputs
                Node pNode = elem.asA<Node>();
                if (pNode != null)
                {
                    if (pNode.getInputCount() != 0)
                    {
                        foreach (Input input in pNode.getInputs())
                        {
                            string type = input.getType();
                            Value value = input.getValue();
                            if (input.hasUnit() && value != null)
                            {
                                if (type == "float")
                                {
                                    float originalval = value.asA<float>();
                                    float convertedValue = uconverter.convert(originalval, input.getUnit(), DISTANCE_DEFAULT);
                                    float reconvert = uconverter.convert(convertedValue, DISTANCE_DEFAULT, input.getUnit());
                                    REQUIRE((originalval - reconvert) < EPSILON);
                                }
                                else if (type == "vector2")
                                {
                                    Vector2 originalval = value.asA<Vector2>();
                                    Vector2 convertedValue = uconverter.convert(originalval, input.getUnit(), DISTANCE_DEFAULT);
                                    Vector2 reconvert = uconverter.convert(convertedValue, DISTANCE_DEFAULT, input.getUnit());
                                    REQUIRE(originalval == reconvert);
                                }
                                else if (type == "vector3")
                                {
                                    Vector3 originalval = value.asA<Vector3>();
                                    Vector3 convertedValue = uconverter.convert(originalval, input.getUnit(), DISTANCE_DEFAULT);
                                    Vector3 reconvert = uconverter.convert(convertedValue, DISTANCE_DEFAULT, input.getUnit());
                                    REQUIRE(originalval == reconvert);
                                }
                                else if (type == "vector4")
                                {
                                    Vector4 originalval = value.asA<Vector4>();
                                    Vector4 convertedValue = uconverter.convert(originalval, input.getUnit(), DISTANCE_DEFAULT);
                                    Vector4 reconvert = uconverter.convert(convertedValue, DISTANCE_DEFAULT, input.getUnit());
                                    REQUIRE(originalval == reconvert);
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
