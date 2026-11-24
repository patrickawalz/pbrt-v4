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
