//
// Copyright Contributors to the MaterialX Project
// SPDX-License-Identifier: Apache-2.0
//

using System;
using System.Collections.Generic;
using System.Text;
using MaterialX;
using Type = MaterialX.Type;
using testing;
using static MaterialX.Mx;
using static MaterialXTest.Catch;

// OslShaderGeneratorTester Definition
public class OslShaderGeneratorTester : GenShaderUtil.ShaderGeneratorTester
{
    public OslShaderGeneratorTester(ShaderGenerator shaderGenerator, List<FilePath> testRootPaths,
                                    FileSearchPath searchPath, FilePath logFilePath, bool writeShadersToDisk) :
        base(shaderGenerator, testRootPaths, searchPath, logFilePath, writeShadersToDisk)
    {
    }

    public override void setTestStages()
    {
        _testStages.Add(Stage.PIXEL);
    }

    // Ignore trying to create shader code for lightshaders
    public override void addSkipNodeDefs()
    {
        _skipNodeDefs.Add("ND_point_light");
        _skipNodeDefs.Add("ND_spot_light");
        _skipNodeDefs.Add("ND_directional_light");
        base.addSkipNodeDefs();
    }

    // Arnold specific files are ignored in vanilla osl target
    public override void addSkipLibraryFiles()
    {
        _skipLibraryFiles.Add("pbrlib_genosl_arnold_impl.mtlx");
    }

    public override void addSkipFiles()
    {
        base.addSkipFiles();
    }

    // Ignore light shaders in the document for OSL
    public override void findLights(Document doc, List<Node> lights)
    {
        lights.Clear();
    }

    // No direct lighting to register for OSL
    public override void registerLights(Document doc, List<Node> lights, GenContext context)
    {
    }

    protected override void getImplementationWhiteList(StringSet whiteList)
    {
        whiteList.Clear();
        whiteList.Add("displacementshader");
        whiteList.Add("volumeshader");
        whiteList.Add("IM_constant_");
        whiteList.Add("IM_dot_");
        whiteList.Add("IM_angle");
        whiteList.Add("IM_geompropvalue");
        whiteList.Add("IM_geompropvalueuniform");
        whiteList.Add("IM_osl_set_ci");
        base.getImplementationWhiteList(whiteList);
    }
}

public static class GenOslTests
{
    [TEST("GenOsl", "GenShaderOSLSyntax")]
    public static void GenOsl_GenShaderOSLSyntax()
    {
        TypeSystem ts = TypeSystem.create();
        Syntax syntax = OslSyntax.create(ts);

        REQUIRE(syntax.getTypeName(Type.FLOAT) == "float");
        REQUIRE(syntax.getTypeName(Type.COLOR3) == "color");
        REQUIRE(syntax.getTypeName(Type.VECTOR3) == "vector");
        REQUIRE(syntax.getTypeName(Type.FLOATARRAY) == "float");
        REQUIRE(syntax.getTypeName(Type.INTEGERARRAY) == "int");
        REQUIRE(Type.FLOATARRAY.isArray());
        REQUIRE(Type.INTEGERARRAY.isArray());

        REQUIRE(syntax.getTypeName(Type.BSDF) == "BSDF");
        REQUIRE(syntax.getOutputTypeName(Type.BSDF) == "output BSDF");

        // Set fixed precision with one digit
        using ScopedFloatFormatting format = new ScopedFloatFormatting(Value.FloatFormat.FloatFormatFixed, 1);

        string value = EMPTY_STRING;
        value = syntax.getDefaultValue(Type.FLOAT);
        REQUIRE(value == "0.0");
        value = syntax.getDefaultValue(Type.COLOR3);
        REQUIRE(value == "color(0.0)");
        value = syntax.getDefaultValue(Type.COLOR3, true);
        REQUIRE(value == "color(0.0)");
        value = syntax.getDefaultValue(Type.COLOR4);
        REQUIRE(value == "color4(color(0.0), 0.0)");
        value = syntax.getDefaultValue(Type.COLOR4, true);
        REQUIRE(value == "{color(0.0), 0.0}");
        value = syntax.getDefaultValue(Type.FLOATARRAY, true);
        REQUIRE(value.Length == 0);
        value = syntax.getDefaultValue(Type.INTEGERARRAY, true);
        REQUIRE(value.Length == 0);

        Value floatValue = Value.createValue<float>(42.0f);
        value = syntax.getValue(Type.FLOAT, floatValue);
        REQUIRE(value == "42.0");
        value = syntax.getValue(Type.FLOAT, floatValue, true);
        REQUIRE(value == "42.0");

        Value color3Value = Value.createValue<Color3>(new Color3(1.0f, 2.0f, 3.0f));
        value = syntax.getValue(Type.COLOR3, color3Value);
        REQUIRE(value == "color(1.0, 2.0, 3.0)");
        value = syntax.getValue(Type.COLOR3, color3Value, true);
        REQUIRE(value == "color(1.0, 2.0, 3.0)");

        Value color4Value = Value.createValue<Color4>(new Color4(1.0f, 2.0f, 3.0f, 4.0f));
        value = syntax.getValue(Type.COLOR4, color4Value);
        REQUIRE(value == "color4(color(1.0, 2.0, 3.0), 4.0)");
        value = syntax.getValue(Type.COLOR4, color4Value, true);
        REQUIRE(value == "{color(1.0, 2.0, 3.0), 4.0}");

        List<float> floatArray = new List<float> { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f };
        Value floatArrayValue = Value.createValue<List<float>>(floatArray);
        value = syntax.getValue(Type.FLOATARRAY, floatArrayValue);
        REQUIRE(value == "{0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7}");

        List<int> intArray = new List<int> { 1, 2, 3, 4, 5, 6, 7 };
        Value intArrayValue = Value.createValue<List<int>>(intArray);
        value = syntax.getValue(Type.INTEGERARRAY, intArrayValue);
        REQUIRE(value == "{1, 2, 3, 4, 5, 6, 7}");
    }

    [TEST("GenOsl", "GenShaderOSLImplementationCheck")]
    public static void GenOsl_GenShaderOSLImplementationCheck()
    {
        GenContext context = new GenContext(OslShaderGenerator.create());

        StringSet generatorSkipNodeTypes = new StringSet();
        generatorSkipNodeTypes.Add("light");

        StringSet generatorSkipNodeDefs = new StringSet();
        GenShaderUtil.checkImplementations(context, generatorSkipNodeTypes, generatorSkipNodeDefs);
    }

    [TEST("GenOsl", "GenShaderOSLUniqueNames")]
    public static void GenOsl_GenShaderOSLUniqueNames()
    {
        GenContext context = new GenContext(OslShaderGenerator.create());
        context.registerSourceCodeSearchPath(getDefaultDataSearchPath());
        GenShaderUtil.testUniqueNames(context, Stage.PIXEL);
    }

    [TEST("GenOsl", "GenShaderOSLMetadata")]
    public static void GenOsl_GenShaderOSLMetadata()
    {
        FileSearchPath searchPath = getDefaultDataSearchPath();
        Document doc = createDocument();
        loadLibraries(new List<FilePath> { new FilePath("libraries") }, searchPath, doc);

        //
        // Define custom attributes to be exported as shader metadata
        //

        AttributeDef adNodeName = doc.addAttributeDef("AD_node_name");
        adNodeName.setType("string");
        adNodeName.setAttrName("node_name");
        adNodeName.setExportable(true);

        AttributeDef adNodeCategory = doc.addAttributeDef("AD_node_category");
        adNodeCategory.setType("string");
        adNodeCategory.setAttrName("node_category");
        adNodeCategory.setExportable(true);

        AttributeDef adNodeTypeId = doc.addAttributeDef("AD_node_type_id");
        adNodeTypeId.setType("integer");
        adNodeTypeId.setAttrName("node_type_id");
        adNodeTypeId.setExportable(true);

        AttributeDef adAttributeLongName = doc.addAttributeDef("AD_attribute_long_name");
        adAttributeLongName.setType("string");
        adAttributeLongName.setAttrName("attribute_long_name");
        adAttributeLongName.setExportable(true);

        AttributeDef adAttributeShortName = doc.addAttributeDef("AD_attribute_short_name");
        adAttributeShortName.setType("string");
        adAttributeShortName.setAttrName("attribute_short_name");
        adAttributeShortName.setExportable(true);

        // Define a non-exportable attribute.
        AttributeDef adShouldNotBeExported = doc.addAttributeDef("AD_should_not_be_exported");
        adShouldNotBeExported.setType("float");
        adShouldNotBeExported.setAttrName("should_not_be_exported");
        adShouldNotBeExported.setExportable(false);

        //
        // Assign metadata on a shader nodedef
        //

        NodeDef stdSurfNodeDef = doc.getNodeDef("ND_standard_surface_surfaceshader");
        REQUIRE(stdSurfNodeDef != null);
        stdSurfNodeDef.setAttribute("node_name", "StandardSurface");
        stdSurfNodeDef.setAttribute("node_category", "shader/surface");
        stdSurfNodeDef.setAttribute("node_type_id", "1234");

        Input baseColor = stdSurfNodeDef.getActiveInput("base_color");
        REQUIRE(baseColor != null);
        baseColor.setAttribute("attribute_long_name", "BaseColor");
        baseColor.setAttribute("attribute_short_name", "bc");
        baseColor.setAttribute("should_not_be_exported", "42");

        //
        // Create an instance of this shader and validate that metadata is exported as expected.
        //

        Node stdSurf1 = doc.addNodeInstance(stdSurfNodeDef, "standardSurface1");
        REQUIRE(stdSurf1 != null);

        ShaderGenerator generator = OslShaderGenerator.create();
        GenContext context = new GenContext(generator);
        context.registerSourceCodeSearchPath(searchPath);

        // Metadata to export must be registered in the context before shader generation starts.
        // Custom generators can override this method to customize which metadata gets registered.
        generator.registerShaderMetadata(doc, context);

        // Generate the shader.
        Shader shader = generator.generate(stdSurf1.getName(), stdSurf1, context);
        REQUIRE(shader != null);
    }

    public static void generateOslCode()
    {
        FileSearchPath searchPath = getDefaultDataSearchPath();

        List<FilePath> testRootPaths = new List<FilePath>();
        testRootPaths.Add(searchPath.find("resources/Materials/TestSuite"));
        testRootPaths.Add(searchPath.find("resources/Materials/Examples"));

        FilePath logPath = new FilePath("genosl_vanilla_generate_test.txt");

        bool writeShadersToDisk = false;
        OslShaderGeneratorTester tester = new OslShaderGeneratorTester(OslShaderGenerator.create(), testRootPaths, searchPath, logPath, writeShadersToDisk);
        tester.addSkipLibraryFiles();

        GenOptions genOptions = new GenOptions();
        FilePath optionsFilePath = searchPath.find("resources/Materials/TestSuite/_options.mtlx");
        tester.validate(genOptions, optionsFilePath);
    }

    [TEST("GenOsl", "GenShaderOSLShaderGeneration")]
    public static void GenOsl_GenShaderOSLShaderGeneration()
    {
        generateOslCode();
    }
}
