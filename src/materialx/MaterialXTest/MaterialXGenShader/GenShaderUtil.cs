//
// Copyright Contributors to the MaterialX Project
// SPDX-License-Identifier: Apache-2.0
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MaterialX;
using testing;
using static MaterialX.Mx;
using MaterialXTest;
using static MaterialXTest.Catch;

public static partial class GenShaderUtil
{
    public const string LAYOUT_SUFFIX = "_layout";
    public const string SOURCE_CODE_STRING = "sourcecode";

    private static string getFileExtensionForTarget(string target)
    {
        if (target == "genglsl")
        {
            return "glsl";
        }
        if (target == "genosl")
        {
            return "osl";
        }
        if (target == "genmdl")
        {
            return "mdl";
        }
        return target;
    }

    // (not in MaterialX) The log files of the tests are written to the working
    // directory upstream. Here they go to the system temporary directory.
    public static FilePath testOutputPath(FilePath path)
    {
        if (path.isAbsolute())
        {
            return path;
        }
        return FilePath.getSystemTemporaryDirectory() / path;
    }

    // What a C++ output stream writes for a value.
    private static string S(string value)
    {
        return value;
    }

    private static string S(bool value)
    {
        return value ? "1" : "0";
    }

    private static string S(int value)
    {
        return Mx.to_string(value);
    }

    private static string S(uint value)
    {
        return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string S(float value)
    {
        return toValueString(value);
    }

    //
    // Get source content, source path and resolved paths for
    // an implementation
    //
    public static bool getShaderSource(GenContext context, Implementation implementation, ref FilePath sourcePath, ref string resolvedSource, ref string sourceContents)
    {
        if (implementation != null)
        {
            resolvedSource = implementation.getAttribute(SOURCE_CODE_STRING);
            if (resolvedSource.Length != 0)
            {
                return true;
            }
            sourcePath = implementation.getFile();
            FilePath localPath = new FilePath(implementation.getSourceUri()).getParentPath();
            FilePath resolvedPath = context.resolveSourceFile(sourcePath, localPath);
            sourceContents = readFile(resolvedPath);
            resolvedSource = resolvedPath.asString();
            return sourceContents.Length != 0;
        }
        return false;
    }

    // Check that implementations exist for all nodedefs supported per generator
    public static void checkImplementations(GenContext context, StringSet generatorSkipNodeTypes, StringSet generatorSkipNodeDefs)
    {

        ShaderGenerator shadergen = context.getShaderGenerator();

        FileSearchPath searchPath = getDefaultDataSearchPath();
        Document doc = createDocument();
        loadLibraries(new List<FilePath> { new FilePath("libraries/targets"), new FilePath("libraries/stdlib"), new FilePath("libraries/pbrlib") }, searchPath, doc);

        string target = shadergen.getTarget();

        string fileName = target + "_implementation_check.txt";

        using StreamWriter implDumpStream = new StreamWriter(testOutputPath(new FilePath(fileName)).asString(), false, new UTF8Encoding(false));

        context.registerSourceCodeSearchPath(searchPath);

        // Node types to explicitly skip temporarily.
        StringSet skipNodeTypes = new StringSet
        {
            "displacement",
            "volume",
            "conical_edf",
            "measured_edf",
            "absorption_vdf",
            "geompropvalue",
            "surfacematerial",
            "volumematerial",
            "osl_set_ci"
        };
        skipNodeTypes.UnionWith(generatorSkipNodeTypes);

        // Explicit set of node defs to skip temporarily
        StringSet skipNodeDefs = new StringSet
        {
            "ND_add_vdf",
            "ND_multiply_vdfF",
            "ND_multiply_vdfC",
            "ND_mix_displacementshader",
            "ND_mix_volumeshader",
            "ND_mix_vdf",
            "ND_surfacematerial",
            "ND_volumematerial",
            "ND_osl_set_ci"
        };
        skipNodeDefs.UnionWith(generatorSkipNodeDefs);

        implDumpStream.Write("-----------------------------------------------------------------------\n");
        implDumpStream.Write("Scanning target: " + S(target) + "\n");

        List<Implementation> impls = doc.getImplementations();
        implDumpStream.Write("-----------------------------------------------------------------------\n");
        implDumpStream.Write("Scanning implementations: " + S(Mx.to_string(impls.Count)) + "\n");
        foreach (Implementation impl in impls)
        {
            NodeDef nodedef = impl.getNodeDef();
            if (nodedef == null)
            {
                string msg = impl.getName();
                string targetName = impl.getTarget();
                if (targetName.Length != 0)
                {
                    msg += ", target: " + targetName;
                }
                string nodedefName = impl.getNodeDefString();
                msg += ": Missing nodedef with name: " + nodedefName;
                implDumpStream.Write(S(msg) + "\n");
            }
        }

        string nodeDefNode = EMPTY_STRING;
        string nodeDefType = EMPTY_STRING;
        int count = 0;
        int missing = 0;
        int skipped = 0;
        string missing_str = EMPTY_STRING;
        string found_str = EMPTY_STRING;

        List<NodeDef> nodedefs = doc.getNodeDefs();
        implDumpStream.Write("-----------------------------------------------------------------------\n");
        implDumpStream.Write("Scanning nodedefs: " + S(Mx.to_string(nodedefs.Count)) + "\n");

        // Scan through every nodedef defined
        foreach (NodeDef nodedef in nodedefs)
        {
            count++;

            string nodeDefName = nodedef.getName();
            string nodeName = nodedef.getNodeString();

            if (skipNodeTypes.Contains(nodeName))
            {
                found_str += "Temporarily skipping implementation required for nodedef: " + nodeDefName + ", Node : " + nodeName + ".\n";
                skipped++;
                continue;
            }
            if (skipNodeDefs.Contains(nodeDefName))
            {
                found_str += "Temporarily skipping implementation required for nodedef: " + nodeDefName + ", Node : " + nodeName + ".\n";
                skipped++;
                continue;
            }

            if (!requiresImplementation(nodedef))
            {
                found_str += "No implementation required for nodedef: " + nodeDefName + ", Node: " + nodeName + ".\n";
                continue;
            }

            InterfaceElement inter = nodedef.getImplementation(target);
            if (inter == null)
            {
                missing++;
                missing_str += "Missing nodedef implementation: " + nodeDefName + ", Node: " + nodeName + ".\n";

                List<InterfaceElement> inters = doc.getMatchingImplementations(nodeDefName);
                foreach (InterfaceElement inter2 in inters)
                {
                    Implementation impl = inter2.asA<Implementation>();
                    if (impl != null)
                    {
                        string msg = "\t Cached Impl: ";
                        msg += impl.getName();
                        msg += ", nodedef: " + impl.getNodeDefString();
                        msg += ", target: " + impl.getTarget();
                        missing_str += msg + ".\n";
                    }
                }

                foreach (Implementation childImpl in impls)
                {
                    if (childImpl.getNodeDefString() == nodeDefName)
                    {
                        string msg = "\t Doc Impl: ";
                        msg += childImpl.getName();
                        msg += ", nodedef: " + childImpl.getNodeDefString();
                        msg += ", target: " + childImpl.getTarget();
                        missing_str += msg + ".\n";
                    }
                }
            }
            else
            {
                Implementation impl = inter.asA<Implementation>();
                if (impl != null)
                {
                    // Test if the generator has an internal implementation first
                    if (shadergen.implementationRegistered(impl.getName()))
                    {
                        found_str += "Found generator impl for nodedef: " + nodeDefName + ", Node: "
                            + nodeDefName + ". Impl: " + impl.getName() + ".\n";
                    }

                    // Check for an implementation explicitly stored
                    else
                    {
                        FilePath sourcePath = new FilePath();
                        string resolvedSource = EMPTY_STRING;
                        string contents = EMPTY_STRING;
                        if (!getShaderSource(context, impl, ref sourcePath, ref resolvedSource, ref contents))
                        {
                            missing++;
                            missing_str += "Missing source code: " + sourcePath.asString() + " for nodedef: "
                                + nodeDefName + ". Impl: " + impl.getName() + ".\n";
                        }
                        else
                        {
                            found_str += "Found impl and src for nodedef: " + nodeDefName + ", Node: "
                                + nodeName + ". Impl: " + impl.getName() + ". Source: " + resolvedSource + ".\n";
                        }
                    }
                }
                else
                {
                    NodeGraph graph = inter.asA<NodeGraph>();
                    found_str += "Found NodeGraph impl for nodedef: " + nodeDefName + ", Node: "
                        + nodeName + ". Graph Impl: " + graph.getName();
                    InterfaceElement graphNodeDefImpl = graph.getImplementation();
                    if (graphNodeDefImpl != null)
                    {
                        found_str += ". Graph Nodedef Impl: " + graphNodeDefImpl.getName();
                    }
                    found_str += ".\n";
                }
            }
        }

        implDumpStream.Write("Missing: " + S(missing) + " implementations out of: " + S(count) + " nodedefs. Skipped: " + S(skipped) + "\n");
        implDumpStream.Write(S(missing_str) + "\n");
        implDumpStream.Write(S(found_str) + "\n");

        // Should have 0 missing including skipped
        if (missing != 0)
        {
            Console.Error.Write(S(("Missing: " + Mx.to_string(missing) + " implementations out of: " + Mx.to_string(count) + " nodedefs. Skipped: " + Mx.to_string(skipped))) + "\n");
            Console.Error.Write(S(("Missing list: " + missing_str)) + "\n");
        }
        REQUIRE(missing == 0);

        implDumpStream.Close();
    }

    // Utility test to  check unique name generation on a shader generator
    public static void testUniqueNames(GenContext context, string stage)
    {
        Document doc = createDocument();

        FileSearchPath searchPath = getDefaultDataSearchPath();
        loadLibraries(new List<FilePath> { new FilePath("libraries/targets"), new FilePath("libraries/stdlib") }, searchPath, doc);

        string exampleName = "unique_names";

        // Generate a shader with an internal node having the same name as the shader,
        // which will result in a name conflict between the shader output and the
        // internal node output
        string shaderName = "unique_names";
        string nodeName = shaderName;

        NodeGraph nodeGraph = doc.addNodeGraph("IMP_" + exampleName);
        Output output1 = nodeGraph.addOutput("out", "color3");
        Node node1 = nodeGraph.addNode("noise2d", nodeName, "color3");

        output1.setConnectedNode(node1);

        ShaderGenerator shadergen = context.getShaderGenerator();

        // Set the output to a restricted name
        string outputQualifier = shadergen.getSyntax().getOutputQualifier();
        output1.setName(outputQualifier);

        GenOptions options;
        Shader shader = shadergen.generate(shaderName, output1, context);
        REQUIRE(shader != null);
        REQUIRE(shader.getSourceCode(stage).Length > 0);

        // Make sure the output and internal node output has their variable names set
        ShaderInput sgOutputSocket = shader.getGraph().getOutputSocket();
        REQUIRE(sgOutputSocket.getVariable() != outputQualifier);
        ShaderNode sgNode1 = shader.getGraph().getNode(node1.getNamePath());
        REQUIRE(sgNode1.getOutput().getVariable() == "unique_names_out");
    }

    // Utility to perform simple performance test to load, validate and generate shaders
    public static void shaderGenPerformanceTest(GenContext context)
    {
        Document nodeLibrary = createDocument();
        FileSearchPath libSearchPath = new FileSearchPath(getDefaultDataSearchPath());

        // Load the standard libraries.
        loadLibraries(new List<FilePath> { new FilePath("libraries") }, libSearchPath, nodeLibrary);
        context.registerSourceCodeSearchPath(libSearchPath);

        // Enable Color Management
        ColorManagementSystem colorManagementSystem = null;
        colorManagementSystem =
            DefaultColorManagementSystem.create(context.getShaderGenerator().getTarget());

        REQUIRE(colorManagementSystem != null);
        if (colorManagementSystem != null)
        {
            context.getShaderGenerator().setColorManagementSystem(colorManagementSystem);
            colorManagementSystem.loadLibrary(nodeLibrary);
        }

        // Enable Unit System
        UnitSystem unitSystem = UnitSystem.create(context.getShaderGenerator().getTarget());
        REQUIRE(unitSystem != null);
        if (unitSystem != null)
        {
            context.getShaderGenerator().setUnitSystem(unitSystem);
            unitSystem.loadLibrary(nodeLibrary);
            // Setup Unit converters
            unitSystem.setUnitConverterRegistry(UnitConverterRegistry.create());
            UnitTypeDef distanceTypeDef = nodeLibrary.getUnitTypeDef("distance");
            unitSystem.getUnitConverterRegistry().addUnitConverter(distanceTypeDef, LinearUnitConverter.create(distanceTypeDef));
            UnitTypeDef angleTypeDef = nodeLibrary.getUnitTypeDef("angle");
            unitSystem.getUnitConverterRegistry().addUnitConverter(angleTypeDef, LinearUnitConverter.create(angleTypeDef));
            context.getOptions().targetDistanceUnit = "meter";
        }

        // Read mtlx documents
        FileSearchPath searchPath = getDefaultDataSearchPath();
        List<FilePath> testRootPaths = new List<FilePath>();
        testRootPaths.Add(searchPath.find("resources/Materials/Examples/StandardSurface"));

        List<Document> loadedDocuments = new List<Document>();
        List<string> documentsPaths = new List<string>();
        List<string> errorLog = new List<string>();

        foreach (FilePath testRoot in testRootPaths)
        {
            loadDocuments(testRoot, libSearchPath, new StringSet(), new StringSet(), loadedDocuments, documentsPaths,
                          null, errorLog);
        }

        REQUIRE(loadedDocuments.Count > 0);
        REQUIRE(loadedDocuments.Count == documentsPaths.Count);

        // Shuffle the order of documents and perform document library import validation and shadergen
        mt19937 rng = new mt19937(0);
        StdRandom.shuffle(loadedDocuments, rng);
        foreach (Document doc in loadedDocuments)
        {
            doc.setDataLibrary(nodeLibrary);
            List<TypedElement> elements = findRenderableElements(doc);

            REQUIRE(elements.Count > 0);

            StringBuilder message = new StringBuilder();
            bool docValid = doc.validate(message);

            REQUIRE(docValid == true);

            context.getShaderGenerator().registerTypeDefs(doc);

            List<string> sourceCode = new List<string>();
            Shader shader = null;
            shader = context.getShaderGenerator().generate(elements[0].getName(), elements[0], context);

            REQUIRE(shader != null);
            REQUIRE(shader.getSourceCode(Stage.PIXEL).Length > 0);
        }
    }

    //
    // Render validation options. Reflects the _options.mtlx
    // file in the test suite area.
    //
    public class TestSuiteOptions
    {
        // Print out options
        public void print(TextWriter output)
        {
            output.Write("Render Test Options:\n");
            output.Write("\tOverride Files: { ");
            foreach (string overrideFile in overrideFiles)
            {
                output.Write(overrideFile + " ");
            }
            output.Write("} \n");
            output.Write("\tLight Setup Files: { ");
            foreach (string lightFile in lightFiles)
            {
                output.Write(lightFile + " ");
            }
            output.Write("} \n");
            output.Write("\tTargets to run: \n");
            foreach (string t in targets)
            {
                output.Write("Target: " + S(t) + "\n");
            }
            output.Write("\tCheck Implementation Usage Count: " + S(checkImplCount) + "\n");
            output.Write("\tDump Generated Code: " + S(dumpGeneratedCode) + "\n");
            output.Write("\tShader Interfaces: " + S(shaderInterfaces) + "\n");
            output.Write("\tRender Size: " + S(renderSize[0]) + "," + S(renderSize[1]) + "\n");
            output.Write("\tDump uniforms and Attributes  " + S(dumpUniformsAndAttributes) + "\n");
            output.Write("\tRender Geometry: " + S(renderGeometry.asString()) + "\n");
            output.Write("\tEnable Direct Lighting: " + S(enableDirectLighting) + "\n");
            output.Write("\tEnable Indirect Lighting: " + S(enableIndirectLighting) + "\n");
            output.Write("\tRadiance IBL File Path " + S(radianceIBLPath.asString()) + "\n");
            output.Write("\tIrradiance IBL File Path: " + S(irradianceIBLPath.asString()) + "\n");
            output.Write("\tExtra library paths: " + S(extraLibraryPaths.asString()) + "\n");
            output.Write("\tRender test paths: " + S(renderTestPaths.asString()) + "\n");
            output.Write("\tRender test exclude files: ");
            bool firstExcludeFile = true;
            foreach (string excludeFile in renderTestExcludeFiles)
            {
                if (!firstExcludeFile)
                {
                    output.Write(",");
                }
                output.Write(excludeFile);
                firstExcludeFile = false;
            }
            output.Write("\n");
            output.Write("\tEnable Reference Quality: " + S(enableReferenceQuality) + "\n");
            output.Write("\tOutput Directory: " + S((outputDirectory.isEmpty() ? "(default)" : outputDirectory.asString())) + "\n");
            output.Write("\tEnable Tracing: " + S(enableTracing) + "\n");
        }

        // Option options from an options file
        public bool readOptions(string optionFile)
        {
            // These strings should make the input names defined in the
            // GenShaderUtil::TestSuiteOptions nodedef in test suite file _options.mtlx
            //
            string RENDER_TEST_OPTIONS_STRING = "TestSuiteOptions";
            string OVERRIDE_FILES_STRING = "overrideFiles";
            string TARGETS_STRING = "targets";
            string LIGHT_FILES_STRING = "lightFiles";
            string SHADER_INTERFACES_STRING = "shaderInterfaces";
            string RENDER_SIZE_STRING = "renderSize";
            string DUMP_UNIFORMS_AND_ATTRIBUTES_STRING = "dumpUniformsAndAttributes";
            string CHECK_IMPL_COUNT_STRING = "checkImplCount";
            string DUMP_GENERATED_CODE_STRING = "dumpGeneratedCode";
            string RENDER_GEOMETRY_STRING = "renderGeometry";
            string ENABLE_DIRECT_LIGHTING = "enableDirectLighting";
            string ENABLE_INDIRECT_LIGHTING = "enableIndirectLighting";
            string RADIANCE_IBL_PATH_STRING = "radianceIBLPath";
            string IRRADIANCE_IBL_PATH_STRING = "irradianceIBLPath";
            string SPHERE_GEOMETRY = "sphere.obj";
            string EXTRA_LIBRARY_PATHS = "extraLibraryPaths";
            string RENDER_TEST_PATHS = "renderTestPaths";
            string RENDER_TEST_EXCLUDE_FILES = "renderTestExcludeFiles";
            string OUTPUT_DIRECTORY_STRING = "outputDirectory";
            string ENABLE_TRACING_STRING = "enableTracing";
            overrideFiles.Clear();
            dumpGeneratedCode = false;
            renderGeometry = SPHERE_GEOMETRY;
            enableDirectLighting = true;
            enableIndirectLighting = true;
            enableReferenceQuality = false;

            Document doc = createDocument();
            try
            {
                readFromXmlFile(doc, optionFile, new FileSearchPath());

                NodeDef optionDefs = doc.getNodeDef(RENDER_TEST_OPTIONS_STRING);
                if (optionDefs != null)
                {
                    foreach (Input p in optionDefs.getInputs())
                    {
                        string name = p.getName();
                        Value val = p.getValue();
                        if (val != null)
                        {
                            if (name == OVERRIDE_FILES_STRING)
                            {
                                overrideFiles = splitString(p.getValueString(), ",");
                            }
                            else if (name == LIGHT_FILES_STRING)
                            {
                                lightFiles = splitString(p.getValueString(), ",");
                            }
                            else if (name == SHADER_INTERFACES_STRING)
                            {
                                shaderInterfaces = val.asA<int>();
                            }
                            else if (name == RENDER_SIZE_STRING)
                            {
                                renderSize = val.asA<Vector2>();
                            }
                            else if (name == DUMP_UNIFORMS_AND_ATTRIBUTES_STRING)
                            {
                                dumpUniformsAndAttributes = val.asA<bool>();
                            }
                            else if (name == TARGETS_STRING)
                            {
                                List<string> list = splitString(p.getValueString(), ",");
                                foreach (string l in list)
                                {
                                    targets.Add(l);
                                }
                            }
                            else if (name == CHECK_IMPL_COUNT_STRING)
                            {
                                checkImplCount = val.asA<bool>();
                            }
                            else if (name == DUMP_GENERATED_CODE_STRING)
                            {
                                dumpGeneratedCode = val.asA<bool>();
                            }
                            else if (name == RENDER_GEOMETRY_STRING)
                            {
                                renderGeometry = p.getValueString();
                            }
                            else if (name == ENABLE_DIRECT_LIGHTING)
                            {
                                enableDirectLighting = val.asA<bool>();
                            }
                            else if (name == ENABLE_INDIRECT_LIGHTING)
                            {
                                enableIndirectLighting = val.asA<bool>();
                            }
                            else if (name == RADIANCE_IBL_PATH_STRING)
                            {
                                radianceIBLPath = p.getValueString();
                            }
                            else if (name == IRRADIANCE_IBL_PATH_STRING)
                            {
                                irradianceIBLPath = p.getValueString();
                            }
                            else if (name == EXTRA_LIBRARY_PATHS)
                            {
                                List<string> list = splitString(p.getValueString(), ",");
                                foreach (string l in list)
                                {
                                    extraLibraryPaths.append(new FilePath(l));
                                }
                            }
                            else if (name == RENDER_TEST_PATHS)
                            {
                                List<string> list = splitString(p.getValueString(), ",");
                                foreach (string l in list)
                                {
                                    renderTestPaths.append(new FilePath(l));
                                }
                            }
                            else if (name == RENDER_TEST_EXCLUDE_FILES)
                            {
                                List<string> list = splitString(p.getValueString(), ",");
                                foreach (string l in list)
                                {
                                    if (l.Length != 0)
                                    {
                                        renderTestExcludeFiles.Add(l);
                                    }
                                }
                            }
                            else if (name == OUTPUT_DIRECTORY_STRING)
                            {
                                string dirPath = p.getValueString();
                                if (dirPath.Length != 0)
                                {
                                    outputDirectory = new FilePath(dirPath);
                                    // Create the directory if it doesn't exist
                                    if (!outputDirectory.exists())
                                    {
                                        outputDirectory.createDirectory(true);
                                    }
                                }
                            }
                            else if (name == ENABLE_TRACING_STRING)
                            {
                                enableTracing = val.asA<bool>();
                            }
                        }
                    }
                }

                // Handle direct and indirect lighting toggles.
                if (!enableDirectLighting)
                {
                    lightFiles.Clear();
                }
                if (!enableIndirectLighting)
                {
                    radianceIBLPath.assign(EMPTY_STRING);
                    irradianceIBLPath.assign(EMPTY_STRING);
                }

                // If there is a filter on the files to run turn off profile checking
                if (overrideFiles.Count != 0)
                {
                    checkImplCount = false;
                }


                return true;
            }
            catch (MaterialX.Exception e)
            {
                Console.Out.Write(S(e.what()));
            }
            return false;
        }

        // Filter list of files to only run validation on.
        public List<string> overrideFiles = new List<string>();

        // List of target identifiers
        public StringSet targets = new StringSet();

        // Comma separated list of light setup files
        public List<string> lightFiles = new List<string>();

        // Set to true to always dump generated code to disk
        public bool dumpGeneratedCode = false;

        // Check the count of number of implementations used
        public bool checkImplCount = true;

        // Run using a set of interfaces:
        // - 3 = run complete + reduced.
        // - 2 = run complete only (default)
        // - 1 = run reduced only.
        public int shaderInterfaces = 2;

        // Render size
        public Vector2 renderSize = new Vector2(512, 512);

        // Set this to be true if it is desired to dump out uniform and attribute information to the logging file.
        public bool dumpUniformsAndAttributes = true;

        // Geometry file to be rendered
        public FilePath renderGeometry = new FilePath();

        // Enable direct lighting. Default is true.
        public bool enableDirectLighting;

        // Enable indirect lighting. Default is true.
        public bool enableIndirectLighting;

        // Radiance IBL file.
        public FilePath radianceIBLPath = new FilePath();

        // Irradiance IBL file.
        public FilePath irradianceIBLPath = new FilePath();

        // Extra library paths
        public FileSearchPath extraLibraryPaths = new FileSearchPath();

        // Render test paths
        public FileSearchPath renderTestPaths = new FileSearchPath();

        // Files to exclude from render tests, matched by basename (e.g. "example.mtlx").
        public StringSet renderTestExcludeFiles = new StringSet();

        // Enable reference quality rendering (set by MATERIALX_TEST_REFERENCE_QUALITY).
        public bool enableReferenceQuality = false;

        // Environment-light sample count for rasterized render tests (GLSL/MSL/Slang).
        // Reference quality uses a higher count to drive sampling noise below the
        // threshold of small visual regressions when comparing across targets.
        public int getRasterEnvSampleCount()
        {
            return enableReferenceQuality ? 4096 : 1024;
        }

        // Per-axis supersample factor for rasterized render tests. The captured
        // image is rendered at (renderSize * factor) and box-downsampled.
        public int getRasterSupersampleFactor()
        {
            return enableReferenceQuality ? 2 : 1;
        }

        // Base directory for all test output artifacts (shaders, images, logs).
        // If empty, use default locations. If set, all artifacts go to this directory.
        public FilePath outputDirectory = new FilePath();

        // Enable Perfetto tracing during render tests (requires MATERIALX_BUILD_PERFETTO_TRACING).
        // Default is false to avoid overhead when not profiling.
        public bool enableTracing = false;

        // Helper to resolve output path for an artifact.
        // If outputDirectory is set, returns outputDirectory/filename.
        // Otherwise returns the original path unchanged.
        public FilePath resolveOutputPath(FilePath path)
        {
            if (outputDirectory.isEmpty())
            {
                return path;
            }
            // Extract just the filename and place it in outputDirectory
            return outputDirectory / path.getBaseName();
        }

        // Bake parameters
        public class BakeSetting
        {
            public string bakeFile = EMPTY_STRING;
            public bool hdr = false;
            public uint resolution = 512;
            public Vector2 uvmin = new Vector2(0.0f);
            public Vector2 uvmax = new Vector2(1.0f);
        }

        public List<BakeSetting> bakeSettings = new List<BakeSetting>();
    }

    // Utility class to handle testing of shader generators.
    // Currently only tests source code generation.
    public abstract class ShaderGeneratorTester
    {
        public ShaderGeneratorTester(ShaderGenerator shaderGenerator, List<FilePath> testRootPaths,
                                     FileSearchPath searchPath, FilePath logFilePath, bool writeShadersToDisk)
        {
            _shaderGenerator = shaderGenerator;
            _targetString = shaderGenerator != null ? shaderGenerator.getTarget() : "NULL";
            _testRootPaths = new List<FilePath>(testRootPaths);
            _searchPath = new FileSearchPath(searchPath);
            _logFilePath = new FilePath(logFilePath);
            _writeShadersToDisk = writeShadersToDisk;
        }

        // Check if testing should be performed based in input options
        public virtual bool runTest(TestSuiteOptions testOptions)
        {
            return testOptions.targets.Contains(_targetString);
        }

        // Stages to test is required from derived class
        public abstract void setTestStages();

        // Add files in to not examine
        public virtual void addSkipFiles()
        {
            // ocio_color_management.mtlx uses color spaces which require OCIO.
            _skipFiles.Add("ocio_color_management.mtlx");
        }

        // Add nodedefs to not examine
        public virtual void addSkipNodeDefs()
        {
        }

        // Add files to be skipped while loading libraries
        public virtual void addSkipLibraryFiles()
        {
        }

        // Add color management
        public virtual void addColorManagement()
        {
            if (_colorManagementSystem == null && _shaderGenerator != null)
            {
                string target = _shaderGenerator.getTarget();
                _colorManagementSystem = DefaultColorManagementSystem.create(target);
                if (_colorManagementSystem == null)
                {
                    _logFile.Write(">> Failed to create color management system for target: " + S(target) + "\n");
                }
                else
                {
                    _shaderGenerator.setColorManagementSystem(_colorManagementSystem);
                    _colorManagementSystem.loadLibrary(_dependLib);
                }
            }
        }

        // Add unit system
        public virtual void addUnitSystem()
        {
            if (_unitSystem == null && _shaderGenerator != null)
            {
                string target = _shaderGenerator.getTarget();
                _unitSystem = UnitSystem.create(target);
                if (_unitSystem == null)
                {
                    _logFile.Write(">> Failed to create unit system for target: " + S(target) + "\n");
                }
                else
                {
                    _shaderGenerator.setUnitSystem(_unitSystem);
                    _unitSystem.loadLibrary(_dependLib);
                    _unitSystem.setUnitConverterRegistry(UnitConverterRegistry.create());
                    UnitTypeDef distanceTypeDef = _dependLib.getUnitTypeDef("distance");
                    _unitSystem.getUnitConverterRegistry().addUnitConverter(distanceTypeDef, LinearUnitConverter.create(distanceTypeDef));
                    _defaultDistanceUnit = "meter";            
                    UnitTypeDef angleTypeDef = _dependLib.getUnitTypeDef("angle");
                    _unitSystem.getUnitConverterRegistry().addUnitConverter(angleTypeDef, LinearUnitConverter.create(angleTypeDef));
                }
            }
        }

        // Add user data
        public void addUserData(string name, GenUserData data)
        {
            _userData[name] = data;
        }

        // Load in dependent libraries
        public virtual void setupDependentLibraries()
        {
            _dependLib = createDocument();

            // Load the standard libraries.
            loadLibraries(new List<FilePath> { new FilePath("libraries") }, _searchPath, _dependLib, _skipLibraryFiles);
        }

        // TODO: Merge the methods below with equivalent methods in LightHandler.
        // From a set of nodes, create a mapping of nodedef identifiers to numbers
        public virtual Dictionary<string, uint> computeLightIdMap(List<Node> nodes)
        {
            Dictionary<string, uint> idMap = new Dictionary<string, uint>();
            uint id = 1;
            foreach (Node node in nodes)
            {
                NodeDef nodedef = node.getNodeDef();
                if (nodedef != null)
                {
                    string name = nodedef.getName();
                    if (!idMap.ContainsKey(name))
                    {
                        idMap[name] = id++;
                    }
                }
            }
            return idMap;
        }

        // Find lights to use based on an input document
        public virtual void findLights(Document doc, List<Node> lights)
        {
            lights.Clear();
            foreach (Node node in doc.getNodes())
            {
                if (node.getType() == LIGHT_SHADER_TYPE_STRING)
                {
                    lights.Add(node);
                }
            }
        }

        // Register light node definitions and light count with a given generation context
        public virtual void registerLights(Document doc, List<Node> lights, GenContext context)
        {
            // (not in MaterialX) Upstream unbinds and binds the light shaders of the
            // hardware shader generators here. MaterialXGenHw is not part of this
            // port, so only the map of light identifiers is computed.
            if (lights.Count != 0)
            {
                // Create a list of unique nodedefs and ids for them
                _lightIdMap = computeLightIdMap(lights);
            }

            // Clamp the number of light sources to the number registered
            uint lightSourceCount = (uint)(lights.Count);
            context.getOptions().hwMaxActiveLightSources = lightSourceCount;
        }

        // Generate source code for a given element and check that code was produced.
        public virtual bool generateCode(GenContext context, string shaderName, TypedElement element,
                                         TextWriter log, List<string> testStages, List<string> sourceCode)
        {
            Shader shader = null;
            try
            {
                shader = context.getShaderGenerator().generate(shaderName, element, context);
            }
            catch (MaterialX.Exception e)
            {
                log.Write(">> Code generation failure: " + S(e.what()) + "\n");
                WARN(e.what() + " in " + shaderName);
                shader = null;
            }
            CHECK(shader != null);
            if (shader == null)
            {
                log.Write(">> Failed to generate shader for element: " + S(element.getNamePath()) + "\n");
                return false;
            }

            bool stageFailed = false;
            foreach (string stage in testStages)
            {
                string code = shader.getSourceCode(stage);
                sourceCode.Add(code);
                bool noSource = code.Length == 0;
                CHECK(!noSource);
                if (noSource)
                {
                    log.Write(">> Failed to generate source code for stage: " + S(stage) + "\n");
                    stageFailed = true;
                }
            }
            return !stageFailed;
        }

        // Run test for source code generation
        public void validate(GenOptions generateOptions, string optionsFilePath)
        {
            // Check for an option file first (before opening log) so we can use outputDirectory
            TestSuiteOptions options = new TestSuiteOptions();
            if (!options.readOptions(optionsFilePath))
            {
                Console.Error.Write("Cannot read options file: " + S(optionsFilePath) + ". Skipping test.\n");
                return;
            }
            // Test has been turned off so just do nothing.
            if (!runTest(options))
            {
                Console.Error.Write("Target: " + S(_targetString) + " not set to run. Skipping test.\n");
                return;
            }


            // Start logging - use outputDirectory if set
            FilePath logPath = options.resolveOutputPath(_logFilePath);
            _logFile = new StreamWriter(testOutputPath(logPath).asString(), false, new UTF8Encoding(false));

            options.print(_logFile);

            // Add files to override the files in the test suite to be examined.
            StringSet overrideFiles = new StringSet();
            foreach (string filterFile in options.overrideFiles)
            {
                overrideFiles.Add(filterFile);
            }

            // Dependent library setup
            setupDependentLibraries();
            addColorManagement();
            addUnitSystem();

            // Test suite setup
            addSkipFiles();

            // Generation setup
            setTestStages();

            // Load in all documents to test
            List<string> errorLog = new List<string>();
            foreach (FilePath testRoot in _testRootPaths)
            {
                loadDocuments(testRoot, _searchPath, _skipFiles, overrideFiles, _documents, _documentPaths, 
                                  null, errorLog);
            }
            CHECK(errorLog.Count == 0);
            foreach (string error in errorLog)
            {
                _logFile.Write(S(error) + "\n");
            }

            // Scan each document for renderable elements and check code generation
            //
            // Map to replace "/" in Element path names with "_".
            StringMap pathMap = new StringMap();
            pathMap["/"] = "_";

            // Add nodedefs to skip when testing
            addSkipNodeDefs();

            // Create our context
            GenContext context = new GenContext(_shaderGenerator);
            context.getOptions().assign(generateOptions);
            // Reapply target-specific defaults after replacing the context options.
            _shaderGenerator.applyDefaultOptions(context.getOptions());
            context.registerSourceCodeSearchPath(_searchPath);

            // Register shader metadata defined in the libraries.
            _shaderGenerator.registerShaderMetadata(_dependLib, context);

            // Define working unit if required
            if (context.getOptions().targetDistanceUnit.Length == 0)
            {
                context.getOptions().targetDistanceUnit = _defaultDistanceUnit;
            }

            // Define target color space if required
            if (context.getOptions().targetColorSpaceOverride.Length == 0)
            {
                context.getOptions().targetColorSpaceOverride = "lin_rec709_scene";
            }

            // Check if a binding context has been set.
            // HW::USER_DATA_BINDING_CONTEXT of MaterialXGenHw
            bool bindingContextUsed = _userData.ContainsKey("udbinding");

            // Map to remove invalid names for files when writing to disk
            StringMap filenameRemap = new StringMap();
            filenameRemap[":"] = "_";

            int documentIndex = 0;
            foreach (Document doc in _documents)
            {
                // Apply optional preprocessing.
                preprocessDocument(doc);
                _shaderGenerator.registerShaderMetadata(doc, context);

                // For each new file clear the implementation cache.
                // Since the new file might contain implementations with names
                // colliding with implementations in previous test cases.
                context.clearNodeImplementations();

                // Set user data
                context.clearUserData();
                foreach (KeyValuePair<string, GenUserData> it in _userData)
                {
                    context.pushUserData(it.Key, it.Value);
                }

                // Add in dependent libraries
                bool importedLibrary = false;
                try
                {
                    doc.setDataLibrary(_dependLib);
                    importedLibrary = true;
                }
                catch (MaterialX.Exception e)
                {
                    _logFile.Write("Failed to import library into file: " + S(_documentPaths[documentIndex]) + ". Error: " + S(e.what()) + "\n");
                    CHECK(importedLibrary);
                    continue;
                }

                // Register typedefs from the document.
                _shaderGenerator.registerTypeDefs(doc);

                // Find and register lights
                findLights(_dependLib, _lights);
                registerLights(_dependLib, _lights, context);

                // Find elements to render in the document
                List<TypedElement> elements = new List<TypedElement>();
                try
                {
                    elements = findRenderableElements(doc);
                }
                catch (MaterialX.Exception e)
                {
                    _logFile.Write("Renderables search errors: " + S(e.what()) + "\n");
                }

                if (elements.Count != 0)
                {
                    _logFile.Write("MTLX Filename :" + S(_documentPaths[documentIndex]) + ". Elements tested: " + S(Mx.to_string(elements.Count)) + "\n");
                    documentIndex++;
                }

                // Perform document validation
                StringBuilder message = new StringBuilder();
                bool docValid = doc.validate(message);
                if (!docValid)
                {
                    string msg = "Document is invalid: [" + doc.getSourceUri() + "] " + message;
                    _logFile.Write(S(msg));
                    WARN(msg);
                }
                CHECK(docValid);

                // Traverse the renderable elements and run the validation step
                int missingNodeDefs = 0;
                int missingImplementations = 0;
                int codeGenerationFailures = 0;
                foreach (TypedElement element in elements)
                {
                    string namePath = element.getNamePath();
                    Output output = element.asA<Output>();
                    Node outputNode = element.asA<Node>();
                    if (output != null)
                    {
                        outputNode = output.getConnectedNode();
                    }

                    NodeDef nodeDef = outputNode.getNodeDef();
                    if (nodeDef != null)
                    {
                        // Allow to skip nodedefs to test if specified
                        string nodeDefName = nodeDef.getName();
                        if (_skipNodeDefs.Contains(nodeDefName))
                        {
                            _logFile.Write(">> Skipped testing nodedef: " + S(nodeDefName) + "\n");
                            continue;
                        }

                        string elementName = replaceSubstrings(namePath, pathMap);
                        elementName = createValidName(elementName);
                        elementName = replaceSubstrings(elementName, filenameRemap);

                        InterfaceElement impl = nodeDef.getImplementation(_shaderGenerator.getTarget());
                        if (impl != null)
                        {
                            _logFile.Write("------------ Run validation with element: " + S(namePath) + "------------\n");

                            List<string> sourceCode = new List<string>();
                            bool generatedCode = false;
                            {
                                generatedCode = generateCode(context, elementName, element, _logFile, _testStages, sourceCode);
                            }

                            // Record implementations tested
                            if (options.checkImplCount)
                            {
                                context.getNodeImplementationNames(_usedImplementations);
                                NodeGraph nodeGraph = impl.asA<NodeGraph>();
                                InterfaceElement nodeGraphImpl = nodeGraph != null ? nodeGraph.getImplementation() : null;
                                _usedImplementations.Add(nodeGraphImpl != null ? nodeGraphImpl.getName() : impl.getName());
                            }

                            if (!generatedCode)
                            {
                                _logFile.Write(">> Failed to generate code for nodedef: " + S(nodeDefName) + "\n");
                                codeGenerationFailures++;
                            }
                            else if (_writeShadersToDisk && sourceCode.Count != 0)
                            {
                                string elementNameSuffix = bindingContextUsed ? LAYOUT_SUFFIX : EMPTY_STRING;
                                FilePath path = doc.getSourceUri();
                                if (!path.isEmpty())
                                {
                                    string testFileName = path[path.size() - 1];
                                    int pos = testFileName.LastIndexOf('.');
                                    if (pos != -1)
                                    {
                                        testFileName = testFileName.Substring(0, pos);
                                    }

                                    path = path.getParentPath() / testFileName;
                                    if (!path.exists())
                                    {
                                        path.createDirectory();
                                    }
                                }
                                else
                                {
                                    FileSearchPath searchPath = getDefaultDataSearchPath();
                                    path = searchPath.isEmpty() ? new FilePath() : searchPath[0];
                                }

                                // Redirect to outputDirectory if set
                                if (!options.outputDirectory.isEmpty())
                                {
                                    FilePath materialDir = path.getBaseName();
                                    path = options.outputDirectory / materialDir;
                                    if (!path.exists())
                                    {
                                        path.createDirectory();
                                    }
                                }

                                List<FilePath> sourceCodePaths = new List<FilePath>();
                                if (sourceCode.Count > 1)
                                {
                                    for (int i=0; i<sourceCode.Count; ++i)
                                    {
                                        FilePath filename = path / (elementName + elementNameSuffix + "." + _testStages[i] + "." + getFileExtensionForTarget(_shaderGenerator.getTarget()));
                                        sourceCodePaths.Add(filename);
                                        _logFile.Write("Write source code: " + S(filename.asString()) + "\n");
                                        File.WriteAllText(filename.asString(), sourceCode[i], new UTF8Encoding(false));
                                    }
                                }
                                else
                                {
                                    path = path / (elementName + "."  
                                        + _shaderGenerator.getTarget() 
                                        + "." + getFileExtensionForTarget(_shaderGenerator.getTarget())
                                        );
                                    sourceCodePaths.Add(path);
                                    _logFile.Write("Write source code: " + S(path.asString()) + "\n");
                                    Console.Out.Write("Write source code: " + S(path.asString()) + "\n");
                                    File.WriteAllText(path.asString(), sourceCode[0], new UTF8Encoding(false));
                                }

                                // Run compile test
                                compileSource(sourceCodePaths);
                            }
                        }
                        else
                        {
                            _logFile.Write(">> Failed to find implementation for nodedef: " + S(nodeDefName) + "\n");
                            missingImplementations++;
                        }
                    }
                    else
                    {
                        _logFile.Write(">> Failed to find nodedef for: " + S(namePath) + "\n");
                        missingNodeDefs++;
                    }
                }

                CHECK(missingNodeDefs == 0);
                CHECK(missingImplementations == 0);
                CHECK(codeGenerationFailures == 0);
            }

            if (options.checkImplCount)
            {
                _logFile.Write("---------------------------------------------------\n");
                checkImplementationUsage(_usedImplementations, context, _logFile);
            }

            // End logging
            if (_logFile != null)
            {
                _logFile.Close();
                _logFile = null;
            }

            // Print effective output directory for easy access (clickable in terminals)
            if (!options.outputDirectory.isEmpty())
            {
                Console.Out.Write("\nTest artifacts written to: " + S(options.outputDirectory.asString()) + "\n");
            }
        }

        // Allow the tester to alter the document, e.g., by flattening file names.
        public virtual void preprocessDocument(Document doc)
        {
        }

        // Compile generated source code. Default implementation does nothing.
        public virtual void compileSource(List<FilePath> sourceCodePaths)
        {
        }

        // Check to see that all implementations have been tested for a given
        // language.
        protected void checkImplementationUsage(StringSet usedImpls, GenContext context, TextWriter stream)
        {
            // Get list of implementations for a given target.
            List<Implementation> targetImpls = new List<Implementation>();
            List<Element> children = new List<Element>(_dependLib.getChildren());
            foreach (Element child in children)
            {
                Implementation impl = child.asA<Implementation>();
                if (impl != null && impl.getTarget() == _shaderGenerator.getTarget())
                {
                    targetImpls.Add(impl);
                }
            }

            StringSet whiteList = new StringSet();
            getImplementationWhiteList(whiteList);

            int implementationUseCount = 0;
            List<string> skippedImplementations = new List<string>();
            List<string> missedImplementations = new List<string>();
            foreach (Implementation targetImpl in targetImpls)
            {
                string implName = targetImpl.getName();

                // Skip white-list items
                bool inWhiteList = false;
                foreach (string w in whiteList)
                {
                    if (implName.IndexOf(w, StringComparison.Ordinal) != -1)
                    {
                        inWhiteList = true;
                        break;
                    }
                }
                if (inWhiteList)
                {
                    skippedImplementations.Add(implName);
                    implementationUseCount++;
                    continue;
                }

                if (usedImpls.Contains(implName))
                {
                    implementationUseCount++;
                    continue;
                }

                if (context.findNodeImplementation(implName) != null)
                {
                    implementationUseCount++;
                    continue;
                }
                missedImplementations.Add(implName);
            }

            int count = targetImpls.Count;
            stream.Write("Tested: " + S(implementationUseCount) + " out of: " + S(count) + " library implementations.\n");
            stream.Write("Skipped: " + S(skippedImplementations.Count) + " implementations.\n");
            if (skippedImplementations.Count != 0)
            {
                foreach (string implName in skippedImplementations)
                {
                    stream.Write("\t" + S(implName) + "\n");
                }
            }
            stream.Write("Untested: " + S(missedImplementations.Count) + " implementations.\n");
            if (missedImplementations.Count != 0)
            {
                foreach (string implName in missedImplementations)
                {
                    stream.Write("\t" + S(implName) + "\n");
                }
                CHECK(implementationUseCount == count);
            }
        }

        // Get implementation "whitelist" for those implementations that have
        // been skipped for checking
        protected virtual void getImplementationWhiteList(StringSet whiteList)
        {
        }

        protected ShaderGenerator _shaderGenerator;
        protected readonly string _targetString;
        protected DefaultColorManagementSystem _colorManagementSystem;

        // Unit system
        protected UnitSystem _unitSystem;
        protected string _defaultDistanceUnit = EMPTY_STRING;
        protected Document _dependLib;

        protected readonly List<FilePath> _testRootPaths;
        protected readonly FileSearchPath _searchPath;
        protected readonly FilePath _logFilePath;
        protected bool _writeShadersToDisk;

        protected StringSet _skipFiles = new StringSet();
        protected StringSet _skipLibraryFiles = new StringSet();
        protected List<Document> _documents = new List<Document>();
        protected List<string> _documentPaths = new List<string>();
        protected TextWriter _logFile;
        protected StringSet _skipNodeDefs = new StringSet();
        protected List<string> _testStages = new List<string>();

        protected List<Node> _lights = new List<Node>();
        protected Dictionary<string, uint> _lightIdMap = new Dictionary<string, uint>();

        protected Dictionary<string, GenUserData> _userData = new Dictionary<string, GenUserData>();
        protected StringSet _usedImplementations = new StringSet();
    }
}
