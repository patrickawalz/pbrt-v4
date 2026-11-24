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
using MaterialXTest;
using static MaterialXTest.Catch;

public static class GenShaderTests
{
    //
    // Base tests
    //
    [TEST("GenShader", "Utilities")]
    public static void GenShader_Utilities()
    {
        // Test simple text substitution
        string test1 = "Look behind you, a $threeheaded $monkey!";
        string result1 = "Look behind you, a mighty pirate!";
        StringMap subst1 = new StringMap { { "$threeheaded", "mighty" }, { "$monkey", "pirate" } };
        tokenSubstitution(subst1, ref test1);
        REQUIRE(test1 == result1);

        // Test uniform name substitution
        // (not in MaterialX) HW::T_ENV_RADIANCE and HW::ENV_RADIANCE of MaterialXGenHw,
        // which is not part of this port.
        const string T_ENV_RADIANCE = "$envRadiance";
        const string ENV_RADIANCE = "u_envRadiance";
        string test2 = "uniform vec3 " + T_ENV_RADIANCE + ";";
        string result2 = "uniform vec3 " + ENV_RADIANCE + ";";
        StringMap subst2 = new StringMap { { T_ENV_RADIANCE, ENV_RADIANCE } };
        tokenSubstitution(subst2, ref test2);
        REQUIRE(test2 == result2);
    }

    [TEST("GenShader", "ValidLibraries")]
    public static void GenShader_ValidLibraries()
    {
        FileSearchPath searchPath = getDefaultDataSearchPath();
        Document doc = createDocument();
        loadLibraries(new List<FilePath> { new FilePath("libraries") }, searchPath, doc);

        StringBuilder validationErrors = new StringBuilder();
        bool valid = doc.validate(validationErrors);
        if (!valid)
        {
            Console.Out.Write(validationErrors + "\n");
        }
        REQUIRE(valid);
    }

    [TEST("GenShader", "DuplicateOutputColorTransforms")]
    public static void GenShader_DuplicateOutputColorTransforms()
    {
        TEST_CASE(() =>
        {
            // (not in MaterialX) Upstream runs this part with the GLSL shader generator, which is
            // not part of this port. The OSL shader generator takes its place.
            FileSearchPath searchPath = getDefaultDataSearchPath();
            Document libraries = createDocument();
            loadLibraries(new List<FilePath> { new FilePath("libraries") }, searchPath, libraries);

            ShaderGenerator shaderGenerator = OslShaderGenerator.create();
            ColorManagementSystem colorManagementSystem =
                DefaultColorManagementSystem.create(shaderGenerator.getTarget());
            colorManagementSystem.loadLibrary(libraries);
            shaderGenerator.setColorManagementSystem(colorManagementSystem);

            GenContext context = new GenContext(shaderGenerator);
            context.registerSourceCodeSearchPath(searchPath);
            context.getOptions().targetColorSpaceOverride = "lin_rec709";

            string docString = @"<?xml version=""1.0""?>
          <materialx version=""1.39"" colorspace=""lin_rec709"">
            <triplanarprojection name=""triplanar"" type=""color3"">
              <input name=""filex"" type=""filename"" value="""" colorspace=""srgb_texture"" />
              <input name=""filey"" type=""filename"" value="""" colorspace=""srgb_texture"" />
              <input name=""filez"" type=""filename"" value="""" colorspace=""srgb_texture"" />
            </triplanarprojection>
            <surface_unlit name=""surface"" type=""surfaceshader"">
              <input name=""emission_color"" type=""color3"" nodename=""triplanar"" />
            </surface_unlit>
          </materialx>";
            Document doc = createDocument();
            readFromXmlString(doc, docString);
            doc.setDataLibrary(libraries);

            SECTION("Identical input color spaces", () =>
            {
                // All three filename inputs request the same output transform.
            });
            SECTION("Different input color spaces", () =>
            {
                doc.getNode("triplanar").getInput("filey").setColorSpace("acescg");
                doc.getNode("triplanar").getInput("filez").setColorSpace("acescg");
            });

            REQUIRE(doc.validate());
            Shader shader = null;
            REQUIRE_NOTHROW(() => { shader = shaderGenerator.generate("surface", doc.getNode("surface"), context); });
            REQUIRE(shader != null);
            int transformCount = 0;
            foreach (ShaderNode node in shader.getGraph().getNodes())
            {
                if (stringEndsWith(node.getName(), "_cm"))
                {
                    ++transformCount;
                    CHECK(node.getImplementation().getName() == "NG_srgb_texture_to_lin_rec709_color3");
                }
            }
            CHECK(transformCount == 1);
        });
    }

    [TEST("GenShader", "DuplicateShaderNodeIDs")]
    public static void GenShader_DuplicateShaderNodeIDs()
    {
        // (not in MaterialX) Upstream runs this part with the GLSL shader generator, which is
        // not part of this port. The OSL shader generator takes its place.
        FileSearchPath searchPath = getDefaultDataSearchPath();
        Document libraries = createDocument();
        loadLibraries(new List<FilePath> { new FilePath("libraries") }, searchPath, libraries);

        ShaderGenerator shaderGenerator = OslShaderGenerator.create();
        GenContext context = new GenContext(shaderGenerator);
        context.registerSourceCodeSearchPath(searchPath);

        NodeDef nodeDef = libraries.getNodeDef("ND_add_float");
        REQUIRE(nodeDef != null);

        ShaderGraph graph = new ShaderGraph(null, "graph", libraries, context);
        ShaderNode firstNode = graph.createNode("add1", "add1", nodeDef, context);
        REQUIRE(firstNode != null);
        CHECK(graph.getNode("add1") == firstNode);
        CHECK(graph.getNodes().Count == 1);

        // The node name may be reused as long as its unique identifier is new.
        REQUIRE_NOTHROW(() => { graph.createNode("add1", "add2", nodeDef, context); });
        CHECK(graph.getNodes().Count == 2);
        CHECK(graph.getNode("add2") != null);

        // Reusing a unique identifier must throw and leave the graph unchanged.
        CHECK_THROWS_AS<ExceptionShaderGenError>(() => { graph.createNode("duplicate", "add1", nodeDef, context); });
        CHECK(graph.getNodes().Count == 2);
        CHECK(graph.getNode("add1") == firstNode);
    }

    [TEST("GenShader", "TypeDescCheck")]
    public static void GenShader_TypeDescCheck()
    {
        TypeSystem ts = TypeSystem.create();
        // (not in MaterialX) Upstream creates the GLSL shader generator here, which is
        // not part of this port. The OSL shader generator takes its place.
        GenContext context = new GenContext(OslShaderGenerator.create(ts));

        // Make sure the standard types are registered
        TypeDesc floatType = ts.getType("float");
        REQUIRE(floatType != Type.NONE);
        REQUIRE(floatType.getBaseType() == TypeDesc.BASETYPE_FLOAT);
        TypeDesc integerType = ts.getType("integer");
        REQUIRE(integerType != Type.NONE);
        REQUIRE(integerType.getBaseType() == TypeDesc.BASETYPE_INTEGER);
        TypeDesc booleanType = ts.getType("boolean");
        REQUIRE(booleanType != Type.NONE);
        REQUIRE(booleanType.getBaseType() == TypeDesc.BASETYPE_BOOLEAN);
        TypeDesc color3Type = ts.getType("color3");
        REQUIRE(color3Type != Type.NONE);
        REQUIRE(color3Type.getBaseType() == TypeDesc.BASETYPE_FLOAT);
        REQUIRE(color3Type.getSemantic() == TypeDesc.SEMANTIC_COLOR);
        REQUIRE(color3Type.isFloat3());
        TypeDesc color4Type = ts.getType("color4");
        REQUIRE(color4Type != Type.NONE);
        REQUIRE(color4Type.getBaseType() == TypeDesc.BASETYPE_FLOAT);
        REQUIRE(color4Type.getSemantic() == TypeDesc.SEMANTIC_COLOR);
        REQUIRE(color4Type.isFloat4());

        // Make sure we can register a new custom type
        string fooTypeName = "foo";
        ts.registerType(fooTypeName, TypeDesc.BASETYPE_FLOAT, TypeDesc.SEMANTIC_COLOR, 5);
        TypeDesc fooType = ts.getType(fooTypeName);
        REQUIRE(fooType != Type.NONE);
        REQUIRE(fooType.getSemantic() == TypeDesc.SEMANTIC_COLOR);

        // Make sure we can register a new type replacing an old type
        ts.registerType(fooTypeName, TypeDesc.BASETYPE_INTEGER, TypeDesc.SEMANTIC_VECTOR, 3);
        fooType = ts.getType(fooTypeName);
        REQUIRE(fooType != Type.NONE);
        REQUIRE(fooType.getSemantic() == TypeDesc.SEMANTIC_VECTOR);

        // Make sure we can't request an unknown type
        REQUIRE(ts.getType("bar") == Type.NONE);
    }

    [TEST("GenShader", "ShaderTranslation")]
    public static void GenShader_ShaderTranslation()
    {
        FileSearchPath searchPath = getDefaultDataSearchPath();
        ShaderTranslator shaderTranslator = ShaderTranslator.create();

        FilePath testPath = searchPath.find("resources/Materials/Examples/StandardSurface");
        foreach (FilePath mtlxFile in testPath.getFilesInDirectory(MTLX_EXTENSION))
        {
            Document doc = createDocument();
            loadLibraries(new List<FilePath> { new FilePath("libraries/targets"), new FilePath("libraries/stdlib"), new FilePath("libraries/pbrlib"), new FilePath("libraries/bxdf") }, searchPath, doc);

            readFromXmlFile(doc, testPath / mtlxFile, searchPath);
            mtlxFile.removeExtension();

            bool translated = false;
            try
            {
                shaderTranslator.translateAllMaterials(doc, "UsdPreviewSurface");
                translated = true;
            }
            catch (MaterialX.Exception e)
            {
                Console.Out.Write("Failed translating: " + (testPath / mtlxFile).asString() + ": " + e.what() + "\n");
            }
            REQUIRE(translated);

            StringBuilder validationErrors = new StringBuilder();
            bool valid = doc.validate(validationErrors);
            if (!doc.validate(validationErrors))
            {
                Console.Out.Write("Shader translation of " + (testPath / mtlxFile).asString() + " failed\n");
                Console.Out.Write("Validation errors: " + validationErrors + "\n");
            }
            REQUIRE(valid);
        }
    }

    [TEST("GenShader", "TransparencyRegressionCheck")]
    public static void GenShader_TransparencyRegressionCheck()
    {
        FileSearchPath searchPath = getDefaultDataSearchPath();
        Document libraries = createDocument();
        loadLibraries(new List<FilePath> { new FilePath("libraries") }, searchPath, libraries);

        FilePath resourcePath = searchPath.find("resources");
        List<string> failedTests = new List<string>();
        List<FilePath> testFiles = new List<FilePath> { 
            "Materials/Examples/StandardSurface/standard_surface_default.mtlx", 
            "Materials/Examples/StandardSurface/standard_surface_glass.mtlx",
            "Materials/TestSuite/libraries/metal/brass_wire_mesh.mtlx"
        };
        List<bool> transparencyTest = new List<bool> { false, true, true };
        for (int i=0; i<testFiles.Count; i++)
        {
            FilePath testFile = resourcePath / testFiles[i];
            bool testValue = transparencyTest[i];

            Document testDoc = createDocument();
            testDoc.setDataLibrary(libraries);

            try
            {
                readFromXmlFile(testDoc, testFile, searchPath);
                List<TypedElement> renderables = findRenderableElements(testDoc);
                foreach (TypedElement renderable in renderables)
                {
                    Node node = renderable.asA<Node>();
                    if (node == null)
                    {
                        continue;
                    }
                    if (testValue != isTransparentSurface(node))
                    {
                        failedTests.Add("File: " + testFile.asString() + ". Element: "
                            + renderable.getNamePath() + " should be:" + Mx.to_string(testValue ? 1 : 0));
                    }
                }
            }
            catch (MaterialX.Exception e)
            {
                INFO("Test failed: " + e.what());
            }
        }
        foreach (string failedTest in failedTests)
        {
            INFO(failedTest);
        }
        CHECK(failedTests.Count == 0);
    }

    public static void testDeterministicGeneration(Document libraries, GenContext context)
    {
        FileSearchPath searchPath = getDefaultDataSearchPath();
        FilePath testFile = searchPath.find("resources/Materials/Examples/StandardSurface/standard_surface_marble_solid.mtlx");
        string testElement = "SR_marble1";

        int numRuns = 10;
        Document[] testDocs = new Document[numRuns];
        string[] sourceCode = new string[numRuns];

        for (int i = 0; i < numRuns; ++i)
        {
            Document testDoc = createDocument();
            readFromXmlFile(testDoc, testFile);
            testDoc.setDataLibrary(libraries);

            // Keep the document alive to make sure
            // new memory is allocated for each run
            testDocs[i] = testDoc;

            Element element = testDoc.getChild(testElement);
            CHECK(element != null);

            Shader shader = context.getShaderGenerator().generate(testElement, element, context);
            sourceCode[i] = shader.getSourceCode();

            if (i > 0)
            {
                // Check if the generated source code is the same
                // for each successive run.
                CHECK(sourceCode[i] == sourceCode[0]);
            }
        }
    }

    [TEST("GenShader", "DeterministicGeneration")]
    public static void GenShader_DeterministicGeneration()
    {
        FileSearchPath searchPath = getDefaultDataSearchPath();
        Document libraries = createDocument();
        loadLibraries(new List<FilePath> { new FilePath("libraries") }, searchPath, libraries);

        {
            GenContext context = new GenContext(OslShaderGenerator.create());
            context.registerSourceCodeSearchPath(searchPath);
            testDeterministicGeneration(libraries, context);
        }
    }

    public static void checkPixelDependencies(Document libraries, GenContext context)
    {
        FileSearchPath searchPath = getDefaultDataSearchPath();
        FilePath testFile = searchPath.find("resources/Materials/Examples/GltfPbr/gltf_pbr_boombox.mtlx");
        string testElement = "Material_boombox";

        Document testDoc = createDocument();
        readFromXmlFile(testDoc, testFile);
        testDoc.setDataLibrary(libraries);

        Element element = testDoc.getChild(testElement);
        CHECK(element != null);

        Shader shader = context.getShaderGenerator().generate(testElement, element, context);
        StringSet dependencies = shader.getStage("pixel").getSourceDependencies();
        foreach (string dependency in dependencies)
        {
            FilePath path = new FilePath(dependency);
            REQUIRE(path.exists() == true);
        }
    }

    [TEST("GenShader", "TrackDependencies")]
    public static void GenShader_TrackDependencies()
    {
        FileSearchPath searchPath = getDefaultDataSearchPath();
        Document libraries = createDocument();
        loadLibraries(new List<FilePath> { new FilePath("libraries") }, searchPath, libraries);

        {
            GenContext context = new GenContext(OslShaderGenerator.create());
            context.registerSourceCodeSearchPath(searchPath);
            checkPixelDependencies(libraries, context);
        }
    }

    private static StringMap results = new StringMap();

    public static void variableTracker(ShaderNode node, GenContext context)
    {
        results["primvar_one"] = "geompropvalue1/geomprop";
        results["primvar_two"] = "geompropvalue2/geomprop";
        results["0"] = "Tworld";
        results["upstream_primvar"] = "constant/value";

        if (node.hasClassification(ShaderNode.Classification.GEOMETRIC))
        {
            ShaderInput geomPropInput = node.getInput("geomprop");
            if (geomPropInput != null && geomPropInput.getValue() != null)
            {
                string prop = geomPropInput.getValue().getValueString();
                REQUIRE(results.ContainsKey(prop));
                REQUIRE(results[prop] == geomPropInput.getPath());
            }
            else
            {
                ShaderInput indexIput = node.getInput("index");
                if (indexIput != null && indexIput.getValue() != null)
                {
                    string prop = indexIput.getValue().getValueString();
                    REQUIRE(results.ContainsKey(prop));
                    REQUIRE(results[prop] == indexIput.getPath());
                }
            }
        }
    }

    [TEST("GenShader", "TrackApplicationVariables")]
    public static void GenShader_TrackApplicationVariables()
    {
        string testDocumentString =
        @"<?xml version=""1.0""?>          <materialx version=""1.38"">          <geompropvalue name=""geompropvalue"" type=""color3"" >             <input name=""geomprop"" type=""string"" uniform=""true"" nodename=""constant"" />          </geompropvalue>          <geompropvalue name=""geompropvalue1"" type=""color3"" >            <input name=""geomprop"" type=""string"" uniform=""true"" value=""primvar_one"" />          </geompropvalue>          <geompropvalue name=""geompropvalue2"" type=""color3"" >            <input name=""geomprop"" type=""string"" uniform=""true"" value=""primvar_two"" />          </geompropvalue>          <multiply name=""multiply"" type=""color3"" >            <input name=""in1"" type=""color3"" nodename=""geompropvalue"" />            <input name=""in2"" type=""color3"" nodename=""geompropvalue1"" />          </multiply>          <add name=""add"" type=""color3""  >            <input name=""in1"" type=""color3"" nodename=""multiply"" />            <input name=""in2"" type=""color3"" nodename=""geompropvalue2"" />          </add>          <standard_surface name=""standard_surface"" type=""surfaceshader"" >            <input name=""base_color"" type=""color3"" nodename=""add"" />          </standard_surface>          <constant name=""constant"" type=""string"" >            <input name=""value"" type=""string"" uniform=""true"" value=""upstream_primvar"" />          </constant>          <surfacematerial name=""surfacematerial"" type=""material"" >            <input name=""surfaceshader"" type=""surfaceshader"" nodename=""standard_surface"" />          </surfacematerial>        </materialx>";

        string testElement = "surfacematerial";

        FileSearchPath searchPath = getDefaultDataSearchPath();
        Document libraries = createDocument();
        loadLibraries(new List<FilePath> { new FilePath("libraries") }, searchPath, libraries);

        Document testDoc = createDocument();
        readFromXmlString(testDoc, testDocumentString);
        testDoc.setDataLibrary(libraries);

        Element element = testDoc.getChild(testElement);
        CHECK(element != null);

        {
            GenContext context = new GenContext(OslShaderGenerator.create());
            context.registerSourceCodeSearchPath(searchPath);
            context.setApplicationVariableHandler(variableTracker);
            Shader shader = context.getShaderGenerator().generate(testElement, element, context);
        }
    }

    [TEST("GenShader", "NoOpColorTransforms")]
    public static void GenShader_NoOpColorTransforms()
    {
        // DefaultColorManagementSystem treats "none" and "data" as requiring no
        // color transformation, regardless of the source/target working space.
        DefaultColorManagementSystem colorManagementSystem =
            DefaultColorManagementSystem.create("genglsl");
        CHECK(colorManagementSystem.isNoOpTransform("none", "lin_rec709_scene"));
        CHECK(colorManagementSystem.isNoOpTransform("data", "lin_rec709_scene"));
        CHECK(colorManagementSystem.isNoOpTransform("lin_rec709_scene", "none"));
        CHECK(colorManagementSystem.isNoOpTransform("lin_rec709_scene", "data"));
        CHECK(!colorManagementSystem.isNoOpTransform("srgb_texture", "lin_rec709_scene"));
        // Any unrecognized color space is not considered a no-op.
        CHECK(!colorManagementSystem.isNoOpTransform("Raw", "lin_rec709_scene"));


        // (not in MaterialX) Upstream runs this part with the GLSL shader generator, which is
        // not part of this port. The OSL shader generator takes its place.
        FileSearchPath searchPath = getDefaultDataSearchPath();
        Document libraries = createDocument();
        loadLibraries(new List<FilePath> { new FilePath("libraries") }, searchPath, libraries);

        GenContext context = new GenContext(OslShaderGenerator.create());
        context.registerSourceCodeSearchPath(searchPath);
        colorManagementSystem = DefaultColorManagementSystem.create(context.getShaderGenerator().getTarget());
        colorManagementSystem.loadLibrary(libraries);
        context.getShaderGenerator().setColorManagementSystem(colorManagementSystem);

        // A color3 tagged "data" is not the working color space, and has no
        // corresponding transform nodedef (e.g. no "data_to_lin_rec709_scene"
        // node exists). If isNoOpTransform() were not consulted before looking
        // up a transform, ShaderGraph::populateColorTransformMap would throw
        // ExceptionShaderGenError("Unsupported color space transform ...").
        string noOpDocString = @"<?xml version=""1.0""?>
      <materialx version=""1.39"" colorspace=""lin_rec709_scene"">
        <constant name=""data_constant"" type=""color3"">
          <input name=""value"" type=""color3"" value=""0.5, 0.5, 0.5"" colorspace=""data"" />
        </constant>
        <surface_unlit name=""data_surface"" type=""surfaceshader"">
          <input name=""emission_color"" type=""color3"" nodename=""data_constant"" />
        </surface_unlit>
      </materialx>";
        Document noOpDoc = createDocument();
        readFromXmlString(noOpDoc, noOpDocString);
        noOpDoc.setDataLibrary(libraries);
        Element noOpElement = noOpDoc.getChild("data_surface");
        REQUIRE(noOpElement != null);
        REQUIRE_NOTHROW(() => { context.getShaderGenerator().generate("data_surface", noOpElement, context); });

        // Sanity check that the harness above is capable of catching a real
        // mismatch: an unrecognized, non-no-op color space with no transform
        // nodedef must still throw.
        string unsupportedDocString = @"<?xml version=""1.0""?>
      <materialx version=""1.39"" colorspace=""lin_rec709_scene"">
        <constant name=""bogus_constant"" type=""color3"">
          <input name=""value"" type=""color3"" value=""0.5, 0.5, 0.5"" colorspace=""bogus_colorspace"" />
        </constant>
        <surface_unlit name=""bogus_surface"" type=""surfaceshader"">
          <input name=""emission_color"" type=""color3"" nodename=""bogus_constant"" />
        </surface_unlit>
      </materialx>";
        Document unsupportedDoc = createDocument();
        readFromXmlString(unsupportedDoc, unsupportedDocString);
        unsupportedDoc.setDataLibrary(libraries);
        Element unsupportedElement = unsupportedDoc.getChild("bogus_surface");
        REQUIRE(unsupportedElement != null);
        REQUIRE_THROWS_AS<ExceptionShaderGenError>(() => { context.getShaderGenerator().generate("bogus_surface", unsupportedElement, context); });
    }

    [TEST("GenShader", "EquivalentColorSpaces")]
    public static void GenShader_EquivalentColorSpaces()
    {
        // A legacy color space name and its color interop equivalent refer to the same
        // color space, so DefaultColorManagementSystem reports no transform between them.
        DefaultColorManagementSystem colorManagementSystem =
            DefaultColorManagementSystem.create("genglsl");
        CHECK(colorManagementSystem.isNoOpTransform("lin_rec709", "lin_rec709"));
        CHECK(colorManagementSystem.isNoOpTransform("lin_rec709", "lin_rec709_scene"));
        CHECK(colorManagementSystem.isNoOpTransform("lin_rec709_scene", "lin_rec709"));
        CHECK(colorManagementSystem.isNoOpTransform("srgb_texture", "srgb_rec709_scene"));
        CHECK(colorManagementSystem.isNoOpTransform("acescg", "lin_ap1_scene"));
        CHECK(!colorManagementSystem.isNoOpTransform("lin_rec709", "acescg"));
        CHECK(!colorManagementSystem.isNoOpTransform("srgb_texture", "lin_rec709_scene"));
        CHECK(!colorManagementSystem.isNoOpTransform("lin_rec709", "bogus_colorspace"));

        FileSearchPath searchPath = getDefaultDataSearchPath();
        Document libraries = createDocument();
        loadLibraries(new List<FilePath> { new FilePath("libraries") }, searchPath, libraries);

        // A document authored in the legacy "lin_rec709" working space, generated for the
        // equivalent "lin_rec709_scene" target, must produce the same graph as a document
        // authored in the target space: no color transform nodes are inserted. The uniform
        // attenuation_color input of glTF PBR is included so that the MDL checks below can
        // confirm that its published value remains uniform.
        string legacyDocString = @"<?xml version=""1.0""?>
      <materialx version=""1.39"" colorspace=""lin_rec709"">
        <gltf_pbr name=""legacy_shader"" type=""surfaceshader"">
          <input name=""base_color"" type=""color3"" value=""0.5, 0.5, 0.5"" />
          <input name=""attenuation_color"" type=""color3"" value=""0, 0, 0"" />
        </gltf_pbr>
        <surfacematerial name=""legacy_material"" type=""material"">
          <input name=""surfaceshader"" type=""surfaceshader"" nodename=""legacy_shader"" />
        </surfacematerial>
      </materialx>";
        Document legacyDoc = createDocument();
        readFromXmlString(legacyDoc, legacyDocString);
        legacyDoc.setDataLibrary(libraries);
        Element legacyElement = legacyDoc.getChild("legacy_material");
        REQUIRE(legacyElement != null);

        // When the source and target color spaces differ, a color transform node is inserted
        // between the published value and the shader input. The transform node's input must
        // carry the same uniform flag as the shader input that it feeds.
        string transformDocString = @"<?xml version=""1.0""?>
      <materialx version=""1.39"" colorspace=""lin_rec709_scene"">
        <gltf_pbr name=""transform_shader"" type=""surfaceshader"">
          <input name=""base_color"" type=""color3"" value=""0.5, 0.5, 0.5"" colorspace=""srgb_texture"" />
          <input name=""attenuation_color"" type=""color3"" value=""0, 0, 0"" colorspace=""srgb_texture"" />
        </gltf_pbr>
        <surfacematerial name=""transform_material"" type=""material"">
          <input name=""surfaceshader"" type=""surfaceshader"" nodename=""transform_shader"" />
        </surfacematerial>
      </materialx>";
        Document transformDoc = createDocument();
        readFromXmlString(transformDoc, transformDocString);
        transformDoc.setDataLibrary(libraries);
        Element transformElement = transformDoc.getChild("transform_material");
        REQUIRE(transformElement != null);

        Func<ShaderGenerator, GenContext> createContext = (ShaderGenerator generator) =>
        {
            GenContext context = new GenContext(generator);
            context.registerSourceCodeSearchPath(searchPath);
            DefaultColorManagementSystem cms = DefaultColorManagementSystem.create(generator.getTarget());
            cms.loadLibrary(libraries);
            generator.setColorManagementSystem(cms);
            context.getOptions().targetColorSpaceOverride = "lin_rec709_scene";
            return context;
        };

        // (not in MaterialX) Upstream runs this part with the GLSL shader generator, which is
        // not part of this port. The OSL shader generator takes its place.
        {
            GenContext context = createContext(OslShaderGenerator.create());

            Shader legacyShader = context.getShaderGenerator().generate("legacy_material", legacyElement, context);
            REQUIRE(legacyShader != null);
            foreach (ShaderNode node in legacyShader.getGraph().getNodes())
            {
                CHECK(!stringEndsWith(node.getName(), "_cm"));
            }

            Shader transformShader = context.getShaderGenerator().generate("transform_material", transformElement, context);
            REQUIRE(transformShader != null);
            ShaderNode baseColorTransform = transformShader.getGraph().getNode("transform_shader_base_color_cm");
            REQUIRE(baseColorTransform != null);
            CHECK(!baseColorTransform.getInput(0).isUniform());
            ShaderNode attenuationColorTransform = transformShader.getGraph().getNode("transform_shader_attenuation_color_cm");
            REQUIRE(attenuationColorTransform != null);
            CHECK(attenuationColorTransform.getInput(0).isUniform());
        }

    }

    [TEST("GenShader", "UserFacingColorSpaceNames")]
    public static void GenShader_UserFacingColorSpaceNames()
    {
        // DefaultColorManagementSystem translates both color interop forum IDs and their
        // deprecated legacy equivalents to the same user-facing display name.
        DefaultColorManagementSystem colorManagementSystem =
            DefaultColorManagementSystem.create("genglsl");
        CHECK(colorManagementSystem.getUserFacingName("lin_rec709_scene") == "Linear Rec.709 (sRGB)");
        CHECK(colorManagementSystem.getUserFacingName("lin_rec709") == "Linear Rec.709 (sRGB)");
        CHECK(colorManagementSystem.getUserFacingName("lin_ap1_scene") == "ACEScg");
        CHECK(colorManagementSystem.getUserFacingName("acescg") == "ACEScg");
        CHECK(colorManagementSystem.getUserFacingName("lin_ap0_scene") == "ACES2065-1");
        CHECK(colorManagementSystem.getUserFacingName("none") == "Data");
        CHECK(colorManagementSystem.getUserFacingName("data") == "Data");
        // An unrecognized color space is returned unchanged.
        CHECK(colorManagementSystem.getUserFacingName("bogus_colorspace") == "bogus_colorspace");

    }
}
