// Compares this port with reference outputs of the MaterialX C++ library for
// every document under a directory: the document written back as XML, its
// validation result, the node definition and implementation of every node,
// and the OSL source of every renderable element. The reference outputs are
// written by the same steps in C++ (see docs/materialx-port.md).

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MaterialX;

public static class MtlxGold
{
    public static int Main(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("usage: mtlxgold <materialx root> <documents directory> <reference directory> [output directory] [--stage core|all]");
            return 2;
        }
        string root = args[0];
        string docs = args[1];
        string gold = args[2];
        string outDir = args.Length > 3 && !args[3].StartsWith("--") ? args[3] : null;
        bool coreOnly = Array.IndexOf(args, "core") >= 0;
        if (Array.IndexOf(args, "extra") >= 0)
        {
            return Extra.run(root, docs, gold, outDir);
        }

        FileSearchPath searchPath = new FileSearchPath(root);
        Document lib = Mx.createDocument();
        List<FilePath> folders = new List<FilePath>();
        folders.Add(new FilePath("libraries"));
        Mx.loadLibraries(folders, searchPath, lib);

        List<string> files = new List<string>(Directory.GetFiles(docs, "*.mtlx", SearchOption.AllDirectories));
        files.Sort(StringComparer.Ordinal);

        int xmlSame = 0;
        int logSame = 0;
        int oslSame = 0;
        int oslTotal = 0;
        List<string> problems = new List<string>();
        foreach (string file in files)
        {
            string rel = Path.GetRelativePath(docs, file);
            string goldBase = Path.Combine(gold, rel);
            StringBuilder log = new StringBuilder();
            string xml = null;
            Dictionary<string, string> sources = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                Document doc = Mx.createDocument();
                FileSearchPath docPath = new FileSearchPath(searchPath);
                docPath.append(new FilePath(file).getParentPath());
                Mx.readFromXmlFile(doc, new FilePath(file), docPath);
                xml = Mx.writeToXmlString(doc);
                doc.setDataLibrary(lib);
                StringBuilder message = new StringBuilder();
                bool valid = doc.validate(message);
                log.Append("valid " + (valid ? "1" : "0") + "\n" + message.ToString() + "\n");
                foreach (Element elem in doc.traverseTree())
                {
                    Node node = elem.asA<Node>();
                    if (node == null)
                    {
                        continue;
                    }
                    NodeDef nd = node.getNodeDef();
                    InterfaceElement impl = nd != null ? nd.getImplementation("genosl") : null;
                    log.Append("node " + node.getNamePath() + " " + (nd != null ? nd.getName() : "-") + " " + (impl != null ? impl.getName() : "-") + "\n");
                }
                if (!coreOnly)
                {
                    Generate.run(doc, lib, searchPath, log, sources);
                }
            }
            catch (System.Exception e)
            {
                log.Append("error " + e.Message + "\n");
                if (Environment.GetEnvironmentVariable("MTLXGOLD_TRACE") != null)
                {
                    Console.Error.WriteLine(rel + ": " + e.ToString());
                }
            }

            string goldXml = File.Exists(goldBase + ".xml") ? File.ReadAllText(goldBase + ".xml") : null;
            if (xml == goldXml)
            {
                xmlSame++;
            }
            else
            {
                problems.Add("xml  " + rel);
            }
            string goldLog = File.ReadAllText(goldBase + ".log");
            if (coreOnly)
            {
                goldLog = coreLines(goldLog);
            }
            if (log.ToString() == goldLog)
            {
                logSame++;
            }
            else
            {
                problems.Add("log  " + rel + "  " + firstDifference(log.ToString(), goldLog));
            }
            foreach (KeyValuePair<string, string> source in sources)
            {
                oslTotal++;
                string goldOsl = goldBase + "." + source.Key + ".osl";
                if (File.Exists(goldOsl) && File.ReadAllText(goldOsl) == source.Value)
                {
                    oslSame++;
                }
                else
                {
                    problems.Add("osl  " + rel + " " + source.Key);
                }
            }
            if (outDir != null)
            {
                string outBase = Path.Combine(outDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(outBase));
                if (xml != null)
                {
                    File.WriteAllText(outBase + ".xml", xml);
                }
                File.WriteAllText(outBase + ".log", log.ToString());
                foreach (KeyValuePair<string, string> source in sources)
                {
                    File.WriteAllText(outBase + "." + source.Key + ".osl", source.Value);
                }
            }
        }

        foreach (string problem in problems)
        {
            Console.WriteLine(problem);
        }
        Console.WriteLine("documents " + files.Count + ", xml identical " + xmlSame + ", log identical " + logSame +
                          ", osl identical " + oslSame + " of " + oslTotal);
        return problems.Count == 0 ? 0 : 1;
    }

    private static string coreLines(string log)
    {
        StringBuilder result = new StringBuilder();
        foreach (string line in log.Split('\n'))
        {
            if (line.StartsWith("renderable ") || line.StartsWith("generated ") || line.StartsWith("failed "))
            {
                continue;
            }
            result.Append(line + "\n");
        }
        string text = result.ToString();
        return text.Substring(0, text.Length - 1);
    }

    private static string firstDifference(string a, string b)
    {
        string[] linesA = a.Split('\n');
        string[] linesB = b.Split('\n');
        for (int i = 0; i < Math.Max(linesA.Length, linesB.Length); i++)
        {
            string la = i < linesA.Length ? linesA[i] : "<end>";
            string lb = i < linesB.Length ? linesB[i] : "<end>";
            if (la != lb)
            {
                return "line " + (i + 1) + ": [" + la + "] expected [" + lb + "]";
            }
        }
        return "";
    }
}

public static class Generate
{
    public static void run(Document doc, Document lib, FileSearchPath searchPath, StringBuilder log, Dictionary<string, string> sources)
    {
        GenContext context = new GenContext(OslShaderGenerator.create());
        context.registerSourceCodeSearchPath(searchPath);
        ShaderGenerator gen = context.getShaderGenerator();
        DefaultColorManagementSystem cms = DefaultColorManagementSystem.create(gen.getTarget());
        cms.loadLibrary(lib);
        gen.setColorManagementSystem(cms);
        UnitSystem units = UnitSystem.create(gen.getTarget());
        units.loadLibrary(lib);
        units.setUnitConverterRegistry(UnitConverterRegistry.create());
        UnitTypeDef distance = lib.getUnitTypeDef("distance");
        units.getUnitConverterRegistry().addUnitConverter(distance, LinearUnitConverter.create(distance));
        UnitTypeDef angle = lib.getUnitTypeDef("angle");
        units.getUnitConverterRegistry().addUnitConverter(angle, LinearUnitConverter.create(angle));
        gen.setUnitSystem(units);
        context.getOptions().targetDistanceUnit = "meter";
        context.getOptions().targetColorSpaceOverride = "lin_rec709";
        gen.registerTypeDefs(doc);
        List<TypedElement> elements = Mx.findRenderableElements(doc);
        foreach (TypedElement element in elements)
        {
            string name = Mx.createValidName(element.getNamePath());
            log.Append("renderable " + element.getNamePath() + " " + name + "\n");
            try
            {
                Shader shader = gen.generate(name, element, context);
                sources[name] = shader.getSourceCode();
                log.Append("generated " + name + "\n");
            }
            catch (System.Exception e)
            {
                log.Append("failed " + name + " " + e.Message + "\n");
                if (Environment.GetEnvironmentVariable("MTLXGOLD_TRACE") != null)
                {
                    Console.Error.WriteLine(name + ": " + e.ToString());
                }
            }
        }
    }
}

// The second set of reference outputs (gold2.cpp): reading with comments and
// newlines, writing without XIncludes, tree and graph functions, values,
// flattening, unit conversion, shader translation, and shader generation with
// other options and with the OSL network generator.
public static class Extra
{
    private static void setup(GenContext context, Document lib, FileSearchPath searchPath)
    {
        context.registerSourceCodeSearchPath(searchPath);
        ShaderGenerator gen = context.getShaderGenerator();
        DefaultColorManagementSystem cms = DefaultColorManagementSystem.create(gen.getTarget());
        cms.loadLibrary(lib);
        gen.setColorManagementSystem(cms);
        UnitSystem units = UnitSystem.create(gen.getTarget());
        units.loadLibrary(lib);
        units.setUnitConverterRegistry(UnitConverterRegistry.create());
        UnitTypeDef distance = lib.getUnitTypeDef("distance");
        units.getUnitConverterRegistry().addUnitConverter(distance, LinearUnitConverter.create(distance));
        UnitTypeDef angle = lib.getUnitTypeDef("angle");
        units.getUnitConverterRegistry().addUnitConverter(angle, LinearUnitConverter.create(angle));
        gen.setUnitSystem(units);
    }

    private static void generateAll(string tag, GenContext context, Document doc, Dictionary<string, string> files, StringBuilder log)
    {
        ShaderGenerator gen = context.getShaderGenerator();
        gen.registerTypeDefs(doc);
        foreach (TypedElement element in Mx.findRenderableElements(doc))
        {
            string name = Mx.createValidName(element.getNamePath());
            try
            {
                Shader shader = gen.generate(name, element, context);
                files["." + tag + "." + name + ".osl"] = shader.getSourceCode();
                log.Append(tag + " generated " + name + "\n");
            }
            catch (System.Exception e)
            {
                // A failed dynamic_cast to a reference throws std::bad_cast in
                // the C++ library; the cast fails here in the same place.
                string what = e is InvalidCastException ? "std::bad_cast" : e.Message;
                log.Append(tag + " failed " + name + " " + what + "\n");
                if (Environment.GetEnvironmentVariable("MTLXGOLD_TRACE") != null)
                {
                    Console.Error.WriteLine(tag + " " + name + ": " + e.ToString());
                }
            }
        }
    }

    public static int run(string root, string docs, string gold, string outDir)
    {
        FileSearchPath searchPath = new FileSearchPath(root);
        Document lib = Mx.createDocument();
        List<FilePath> folders = new List<FilePath>();
        folders.Add(new FilePath("libraries"));
        Mx.loadLibraries(folders, searchPath, lib);

        List<string> paths = new List<string>(Directory.GetFiles(docs, "*.mtlx", SearchOption.AllDirectories));
        paths.Sort(StringComparer.Ordinal);

        int same = 0;
        int total = 0;
        Dictionary<string, int> sameByKind = new Dictionary<string, int>(StringComparer.Ordinal);
        Dictionary<string, int> totalByKind = new Dictionary<string, int>(StringComparer.Ordinal);
        List<string> problems = new List<string>();
        foreach (string file in paths)
        {
            string rel = Path.GetRelativePath(docs, file);
            string goldBase = Path.Combine(gold, rel);
            StringBuilder log = new StringBuilder();
            Dictionary<string, string> files = new Dictionary<string, string>(StringComparer.Ordinal);
            FileSearchPath docPath = new FileSearchPath(searchPath);
            docPath.append(new FilePath(file).getParentPath());
            try
            {
                // Reading with comments and newlines.
                {
                    XmlReadOptions readOptions = new XmlReadOptions();
                    readOptions.readComments = true;
                    readOptions.readNewlines = true;
                    Document full = Mx.createDocument();
                    Mx.readFromXmlFile(full, new FilePath(file), docPath, readOptions);
                    files[".full.xml"] = Mx.writeToXmlString(full);
                }

                Document doc = Mx.createDocument();
                Mx.readFromXmlFile(doc, new FilePath(file), docPath);
                {
                    XmlWriteOptions writeOptions = new XmlWriteOptions();
                    writeOptions.writeXIncludeEnable = false;
                    files[".noxi.xml"] = Mx.writeToXmlString(doc, writeOptions);
                }
                doc.setDataLibrary(lib);
                files[".pretty"] = Mx.prettyPrint(doc);

                // Tree and graph functions.
                (int, int) version = doc.getVersionIntegers();
                log.Append("version " + version.Item1 + " " + version.Item2 + "\n");
                foreach (string uri in doc.getReferencedSourceUris())
                {
                    log.Append("uri " + new FilePath(uri).getBaseName() + "\n");
                }
                foreach (Output output in doc.getMaterialOutputs())
                {
                    log.Append("materialoutput " + output.getNamePath() + "\n");
                }
                {
                    Document copy = doc.copy();
                    ElementEquivalenceOptions options = new ElementEquivalenceOptions();
                    StringBuilder message = new StringBuilder();
                    bool equivalent = doc.isEquivalent(copy, options, message);
                    log.Append("equivalent " + (equivalent ? "1" : "0") + " equal " + (doc.isEqual(copy) ? "1" : "0") + "\n" + message.ToString());
                }
                foreach (Element elem in doc.traverseTree())
                {
                    ValueElement valueElem = elem.asA<ValueElement>();
                    if (valueElem != null)
                    {
                        Value value = valueElem.getValue();
                        Value defaultValue = valueElem.getDefaultValue();
                        log.Append("value " + elem.getNamePath() + " [" + (value != null ? value.getTypeString() : "-") + "] [" + (value != null ? value.getValueString() : "-")
                                   + "] [" + valueElem.getResolvedValueString() + "] [" + (defaultValue != null ? defaultValue.getValueString() : "-") + "] ["
                                   + valueElem.getActiveUnit() + "] [" + elem.getActiveColorSpace() + "]\n");
                    }
                    Node node = elem.asA<Node>();
                    if (node != null)
                    {
                        log.Append("node " + node.getNamePath() + " downstream");
                        foreach (PortElement port in node.getDownstreamPorts())
                        {
                            log.Append(" " + port.getNamePath());
                        }
                        log.Append(" outputs");
                        foreach (Output output in Mx.getConnectedOutputs(node))
                        {
                            log.Append(" " + output.getNamePath());
                        }
                        log.Append("\n");
                        if (node.getType() == Mx.MATERIAL_TYPE_STRING)
                        {
                            log.Append("shadernodes " + node.getNamePath());
                            foreach (Node shaderNode in Mx.getShaderNodes(node))
                            {
                                log.Append(" " + shaderNode.getNamePath());
                            }
                            log.Append(" |");
                            foreach (Node shaderNode in Mx.getShaderNodes(node, Mx.SURFACE_SHADER_TYPE_STRING, "genosl"))
                            {
                                log.Append(" " + shaderNode.getNamePath());
                            }
                            log.Append(" | bindings");
                            foreach (MaterialAssign assign in Mx.getGeometryBindings(node))
                            {
                                log.Append(" " + assign.getNamePath());
                            }
                            log.Append("\n");
                        }
                    }
                    GraphElement graph = elem.asA<GraphElement>();
                    if (graph != null)
                    {
                        log.Append("topo " + (graph.getNamePath().Length == 0 ? "<document>" : graph.getNamePath()));
                        foreach (Element sorted in graph.topologicalSort())
                        {
                            log.Append(" " + sorted.getName());
                        }
                        log.Append("\n");
                        string dotName = graph.getNamePath().Length == 0 ? "document" : Mx.createValidName(graph.getNamePath());
                        files[".dot." + dotName] = graph.asStringDot();
                    }
                    Output graphOutput = elem.asA<Output>();
                    if (graphOutput != null)
                    {
                        log.Append("output " + graphOutput.getNamePath() + " cycle " + (graphOutput.hasUpstreamCycle() ? "1" : "0") + " edges");
                        foreach (Edge edge in graphOutput.traverseGraph())
                        {
                            log.Append(" " + edge.getUpstreamElement().getName());
                        }
                        Node world = Mx.connectsToWorldSpaceNode(graphOutput);
                        log.Append(" world " + (world != null ? world.getName() : "-") + "\n");
                    }
                }

                // Flattening.
                try
                {
                    Document flat = doc.copy();
                    flat.flattenSubgraphs();
                    foreach (NodeGraph graph in flat.getNodeGraphs())
                    {
                        if (graph.getActiveSourceUri() == flat.getSourceUri())
                        {
                            graph.flattenSubgraphs();
                        }
                    }
                    files[".flat.xml"] = Mx.writeToXmlString(flat);
                }
                catch (MaterialX.Exception e)
                {
                    log.Append("flatten error " + e.Message + "\n");
                }

                // Unit conversion.
                try
                {
                    Document converted = doc.copy();
                    UnitConverterRegistry registry = UnitConverterRegistry.create();
                    UnitTypeDef distance = lib.getUnitTypeDef("distance");
                    registry.addUnitConverter(distance, LinearUnitConverter.create(distance));
                    log.Append("convert " + (registry.convertToUnit(converted, "distance", "meter") ? "1" : "0") + "\n");
                    files[".units.xml"] = Mx.writeToXmlString(converted);
                }
                catch (MaterialX.Exception e)
                {
                    log.Append("convert error " + e.Message + "\n");
                }

                // Shader translation.
                foreach (string dest in new string[] { "gltf_pbr", "UsdPreviewSurface", "open_pbr_surface", "standard_surface" })
                {
                    try
                    {
                        Document translated = doc.copy();
                        ShaderTranslator.create().translateAllMaterials(translated, dest);
                        files[".to_" + dest + ".xml"] = Mx.writeToXmlString(translated);
                        log.Append("translate " + dest + " ok\n");
                    }
                    catch (MaterialX.Exception e)
                    {
                        log.Append("translate " + dest + " error " + e.Message + "\n");
                    }
                }

                // Transparency and shading queries.
                foreach (TypedElement element in Mx.findRenderableElements(doc))
                {
                    log.Append("query " + element.getNamePath() + " transparent " + (Mx.isTransparentSurface(element, "genosl") ? "1" : "0")
                               + " shading " + (Mx.elementRequiresShading(element) ? "1" : "0") + "\n");
                }
                foreach (TypedElement element in Mx.findRenderableMaterialNodes(doc))
                {
                    log.Append("materialnode " + element.getNamePath() + "\n");
                }

                // Generation with other options.
                {
                    GenContext context = new GenContext(OslShaderGenerator.create());
                    setup(context, lib, searchPath);
                    context.getOptions().targetDistanceUnit = "centimeter";
                    context.getOptions().targetColorSpaceOverride = "lin_rec709";
                    context.getOptions().shaderInterfaceType = ShaderInterfaceType.SHADER_INTERFACE_REDUCED;
                    context.getOptions().fileTextureVerticalFlip = true;
                    context.getOptions().elideConstantNodes = false;
                    generateAll("reduced", context, doc, files, log);
                }
                {
                    GenContext context = new GenContext(OslShaderGenerator.create());
                    setup(context, lib, searchPath);
                    context.getOptions().targetDistanceUnit = "meter";
                    context.getOptions().emitColorTransforms = false;
                    context.getOptions().premultipliedBsdfAdd = true;
                    context.getOptions().distributeLayerOverBsdfMix = true;
                    context.getOptions().oslImplicitSurfaceShaderConversion = false;
                    context.getOptions().addUpstreamDependencies = false;
                    generateAll("refactor", context, doc, files, log);
                }
                {
                    GenContext context = new GenContext(OslNetworkShaderGenerator.create());
                    setup(context, lib, searchPath);
                    context.getOptions().targetDistanceUnit = "meter";
                    context.getOptions().oslConnectCiWrapper = true;
                    generateAll("network", context, doc, files, log);
                }
            }
            catch (System.Exception e)
            {
                log.Append("error " + e.Message + "\n");
                if (Environment.GetEnvironmentVariable("MTLXGOLD_TRACE") != null)
                {
                    Console.Error.WriteLine(rel + ": " + e.ToString());
                }
            }

            files[".log2"] = log.ToString();
            HashSet<string> expected = new HashSet<string>(StringComparer.Ordinal);
            string directory = Path.GetDirectoryName(goldBase);
            string prefix = Path.GetFileName(goldBase);
            foreach (string goldFile in Directory.GetFiles(directory, prefix + ".*"))
            {
                expected.Add(Path.GetFileName(goldFile).Substring(prefix.Length));
            }
            foreach (string suffix in expected)
            {
                string kind = kindOf(suffix);
                totalByKind[kind] = (totalByKind.TryGetValue(kind, out int t) ? t : 0) + 1;
                total++;
                string goldText = File.ReadAllText(goldBase + suffix);
                if (files.TryGetValue(suffix, out string text) && text == goldText)
                {
                    same++;
                    sameByKind[kind] = (sameByKind.TryGetValue(kind, out int n) ? n : 0) + 1;
                }
                else
                {
                    problems.Add(kind + "  " + rel + suffix + (text == null ? " (not produced)" : ""));
                }
            }
            foreach (string suffix in files.Keys)
            {
                if (!expected.Contains(suffix))
                {
                    problems.Add("extra  " + rel + suffix);
                }
            }
            if (outDir != null)
            {
                string outBase = Path.Combine(outDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(outBase));
                foreach (KeyValuePair<string, string> produced in files)
                {
                    File.WriteAllText(outBase + produced.Key, produced.Value);
                }
            }
        }

        foreach (string problem in problems)
        {
            Console.WriteLine(problem);
        }
        foreach (KeyValuePair<string, int> kind in totalByKind)
        {
            Console.WriteLine(kind.Key + ": identical " + (sameByKind.TryGetValue(kind.Key, out int n) ? n : 0) + " of " + kind.Value);
        }
        Console.WriteLine("documents " + paths.Count + ", files identical " + same + " of " + total);
        return problems.Count == 0 ? 0 : 1;
    }

    private static string kindOf(string suffix)
    {
        string[] parts = suffix.Split('.');
        if (suffix.EndsWith(".osl"))
        {
            return "osl " + parts[1];
        }
        if (parts[1] == "dot")
        {
            return "dot";
        }
        return suffix.Substring(1);
    }
}
