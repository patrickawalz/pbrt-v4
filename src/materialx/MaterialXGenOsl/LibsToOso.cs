//
// Copyright Contributors to the MaterialX Project
// SPDX-License-Identifier: Apache-2.0
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using MaterialX;
using static MaterialX.Mx;

// ExceptionCompileError Definition
public class ExceptionCompileError : MaterialX.Exception
{
    // ExceptionCompileError Public Methods
    public ExceptionCompileError(string msg) : base(msg)
    {
        _errorLog = new List<string>();
    }

    public ExceptionCompileError(string msg, List<string> errorLog) : base(msg)
    {
        _errorLog = errorLog;
    }

    public List<string> errorLog()
    {
        return _errorLog;
    }

    // ExceptionCompileError Private Members
    private List<string> _errorLog;
}

// A set of options for controlling the behavior of OSL compilation.
public class OslCompileOptions
{
    // OslCompileOptions Public Members
    public FilePath oslCompilerPath = new FilePath();
    public FileSearchPath oslIncludePath = new FileSearchPath();
    public bool useOslComp = false;
    public bool writeSourceToDisk = true;
}

public static class LibsToOso
{
    private const string argOptions =
        " Options: \n" +
        "    --outputOsoPath [DIRPATH]       TODO\n" +
        "    --libraryRelativeOsoPath [DIRPATH]       TODO\n" +
        "    --outputMtlxPath [DIRPATH]      TODO\n" +
        "    --oslCompilerPath [FILEPATH]    TODO\n" +
        "    --skipWritingOSLSource          TODO\n" +
        "    --skipWritingMtlxDoc            TODO\n" +
        "    --oslIncludePath [DIRPATH]      TODO\n" +
        "    --path [FILEPATH]              Specify an additional data search path location (e.g. '/projects/MaterialX').  This absolute path will be queried when locating data libraries, XInclude references, and referenced images.\n" +
        "    --library [FILEPATH]           Specify an additional data library folder (e.g. 'vendorlib', 'studiolib').  This relative path will be appended to each location in the data search path when loading data libraries.\n" +
        "    --osoNameStrategy [STRING]      TODO - either 'implementation' or 'nodedef' (default:'implementation')\n" +
        "    --texVerticalFlip [BOOLEAN]     Specify whether file texture lookups in the generated modules are flipped vertically, compensating for hosts that sample images with their origin at the upper left (defaults to true)\n" +
        "    --help                          Display the complete list of command-line options\n";

    // The equivalent of std::system: the command is run by the shell and the
    // exit code is returned.
    private static int system(string command)
    {
        ProcessStartInfo startInfo = new ProcessStartInfo();
        if (OperatingSystem.IsWindows())
        {
            startInfo.FileName = "cmd.exe";
            startInfo.ArgumentList.Add("/c");
        }
        else
        {
            startInfo.FileName = "/bin/sh";
            startInfo.ArgumentList.Add("-c");
        }
        startInfo.ArgumentList.Add(command);
        startInfo.UseShellExecute = false;
        using Process process = Process.Start(startInfo);
        process.WaitForExit();
        return process.ExitCode;
    }

    public static bool compileOSL(string oslSourceCode, FilePath oslFilePath, OslCompileOptions options)
    {
        if (!options.useOslComp && !options.writeSourceToDisk)
        {
            throw new MaterialX.Exception("If OslComp library is not being used the source must be written to disk");
        }

        if (options.writeSourceToDisk)
        {
            File.WriteAllText(oslFilePath.asString(), oslSourceCode, new UTF8Encoding(false));
        }

        FilePath osoFilePath = new FilePath(oslFilePath);
        osoFilePath.removeExtension();
        osoFilePath.addExtension("oso");

        // build up a vector of compiler arguments that will be
        // used in both compiler modes.
        List<string> oslCompilerArgs = new List<string>();
        oslCompilerArgs.Add("-o");
        oslCompilerArgs.Add(osoFilePath.asString());
        foreach (FilePath p in options.oslIncludePath)
        {
            oslCompilerArgs.Add("-I" + p.asString() + "");
        }

        {
            // If no command and include path specified then skip checking.
            if (options.oslCompilerPath.isEmpty())
            {
                throw new MaterialX.Exception("OSL compiler path missing");
            }
            if (!options.oslCompilerPath.exists())
            {
                throw new MaterialX.Exception("OSL compiler doesn't exist at '" + options.oslCompilerPath.asString() + "'");
            }

            // Use a known error file name to check
            string errorFile = osoFilePath.asString() + "_compile_errors.txt";

            // Run the command and get back the result. If non-empty string throw exception with error
            string command = options.oslCompilerPath.asString() + " -q " + oslFilePath.asString();
            foreach (string arg in oslCompilerArgs)
            {
                command += " " + arg;
            }

            int returnValue = system(command);

            string result = File.Exists(errorFile) ? File.ReadAllText(errorFile) : EMPTY_STRING;

            if (result.Length != 0)
            {
                List<string> errors = new List<string>();
                errors.Add("Command string: " + command);
                errors.Add("Command return code: " + to_string(returnValue));
                errors.Add("Shader failed to compile:");
                errors.Add(result);
                throw new ExceptionCompileError("OSL compilation error", errors);
            }
        }

        return true;
    }

    public static int Main(string[] args)
    {
        List<string> tokens = new List<string>();

        // Gather the provided arguments.
        for (int i = 0; i < args.Length; i++)
        {
            tokens.Add(args[i]);
        }

        FileSearchPath argSearchPath = getDefaultDataSearchPath();
        List<FilePath> argLibraryFolders = new List<FilePath>();
        string argOutputOsoPath = EMPTY_STRING;
        string argOutputMtlxPath = EMPTY_STRING;
        string argLibraryRelativeOsoPath = EMPTY_STRING;
        string argOslCompilerPath = EMPTY_STRING;
        string argOslIncludePath = EMPTY_STRING;
        string argOsoNameStrategy = "implementation";
        bool argSkipWritingSource = false;
        bool argSkipWritingMtlxDoc = false;
        bool argTexVerticalFlip = true;

        // Loop over the provided arguments, and store their associated values.
        for (int i = 0; i < tokens.Count; i++)
        {
            string token = tokens[i];
            string nextToken = i + 1 < tokens.Count ? tokens[i + 1] : EMPTY_STRING;

            if (token == "--outputOsoPath")
            {
                argOutputOsoPath = nextToken;
            }
            else if (token == "--outputMtlxPath")
            {
                argOutputMtlxPath = nextToken;
            }
            else if (token == "--libraryRelativeOsoPath")
            {
                argLibraryRelativeOsoPath = nextToken;
            }
            else if (token == "--oslCompilerPath")
            {
                argOslCompilerPath = nextToken;
            }
            else if (token == "--oslIncludePath")
            {
                argOslIncludePath = nextToken;
            }
            else if (token == "--path")
            {
                argSearchPath.append(new FileSearchPath(nextToken));
            }
            else if (token == "--library")
            {
                argLibraryFolders.Add(new FilePath(nextToken));
            }
            else if (token == "--osoNameStrategy")
            {
                argOsoNameStrategy = nextToken;
            }
            else if (token == "--texVerticalFlip")
            {
                Value value = Value.createValueFromStrings(nextToken, "boolean");
                if (value != null)
                {
                    argTexVerticalFlip = value.asA<bool>();
                }
                else
                {
                    Console.Out.Write("Unable to parse boolean value for --texVerticalFlip: " + nextToken + "\n");
                }
            }
            else if (token == "--skipWritingOSLSource")
            {
                argSkipWritingSource = true;
            }
            else if (token == "--skipWritingMtlxDoc")
            {
                argSkipWritingMtlxDoc = true;
            }
            else if (token == "--help")
            {
                Console.Out.Write("MaterialXGenOslNetwork - LibsToOso version " + getVersionString());
                Console.Out.Write("\n");
                Console.Out.Write(argOptions + "\n");

                return 0;
            }
            else
            {
                Console.Out.Write("Unrecognized command-line option: " + token + "\n");
                Console.Out.Write("Run LibsToOso with '--help' for a complete list of supported " +
                                  "options." + "\n");

                continue;
            }

            if (nextToken.Length == 0)
            {
                Console.Out.Write("Expected another token following command-line option: " + token + "\n");
            }
            else
            {
                i++;
            }
        }

        if (!(argOsoNameStrategy == "implementation" || argOsoNameStrategy == "nodedef"))
        {
            Console.Error.Write("Unrecognized value for --osoNameStrategy '" + argOsoNameStrategy + "'. Must be 'implementation' or 'nodedef'" + "\n");
            return 1;
        }

        // Append the standard library folder, giving it a lower precedence than user-supplied libraries.
        argLibraryFolders.Add(new FilePath("libraries"));

        // Ensure we have a valid output path.
        FilePath outputOsoPath = new FilePath(argOutputOsoPath);
        if (!outputOsoPath.exists() || !outputOsoPath.isDirectory())
        {
            outputOsoPath.createDirectory();

            if (!outputOsoPath.exists() || !outputOsoPath.isDirectory())
            {
                Console.Error.Write("Failed to find and/or create the provided output oso " +
                                    "path: " + outputOsoPath.asString() + "\n");

                return 1;
            }
        }

        FilePath outputMtlxPath = new FilePath(argOutputMtlxPath);
        if (argSkipWritingMtlxDoc && (!outputMtlxPath.exists() || !outputMtlxPath.isDirectory()))
        {
            outputMtlxPath.createDirectory();

            if (!outputMtlxPath.exists() || !outputMtlxPath.isDirectory())
            {
                Console.Error.Write("Failed to find and/or create the provided output Mtlx " +
                                    "path: " + outputMtlxPath.asString() + "\n");

                return 1;
            }
        }

        // Ensure we have a valid path to the OSL compiler.
        FilePath oslCompilerPath = new FilePath(argOslCompilerPath);
        if (!oslCompilerPath.exists())
        {
            Console.Error.Write("The provided path to the OSL compiler is not valid: " + oslCompilerPath.asString() + "\n");
            return 1;
        }

        // Ensure we have a valid path to the OSL includes.
        FilePath oslIncludePath = new FilePath(argOslIncludePath);
        if (!oslIncludePath.exists() || !oslIncludePath.isDirectory())
        {
            Console.Error.Write("The provided path to the OSL includes is not valid: " + oslIncludePath.asString() + "\n");
            return 1;
        }

        // Create the libraries search path and document.
        Document librariesDoc = createDocument();
        try
        {
            loadLibraries(argLibraryFolders, argSearchPath, librariesDoc);
        }
        catch (System.Exception e)
        {
            Console.Error.Write("Failed to load standard data libraries: " + e.Message + "\n");
            return 1;
        }

        const string target = "genoslnetwork";
        Document implMtlxDoc = null;
        if (!argSkipWritingMtlxDoc)
        {
            implMtlxDoc = createDocument();
        }

        // Build the list of include paths that will be used to compile the shader.
        FileSearchPath oslRendererIncludePaths = new FileSearchPath();
        // Add the provided OSL include path.
        oslRendererIncludePaths.append(oslIncludePath);
        // Add the MaterialX's OSL include path.
        oslRendererIncludePaths.append(argSearchPath.find(new FilePath("libraries/stdlib/genosl/include")));

        // Create the OSL shader generator.
        ShaderGenerator oslShaderGen = OslShaderGenerator.create();

        // Register types from the libraries on the OSL shader generator.
        oslShaderGen.registerTypeDefs(librariesDoc);

        // Setup the context of the OSL shader generator, with the vertical flip
        // of file texture lookups controlled by the --texVerticalFlip option.
        // TODO: Consider generating modules for both image orientations, allowing
        // the orientation to be selected at network generation time.
        GenContext context = new GenContext(oslShaderGen);
        context.registerSourceCodeSearchPath(argSearchPath);
        context.getOptions().addUpstreamDependencies = false;
        context.getOptions().fileTextureVerticalFlip = argTexVerticalFlip;
        context.getOptions().oslImplicitSurfaceShaderConversion = false;

        OslCompileOptions options = new OslCompileOptions();
        options.oslIncludePath = new FileSearchPath(oslRendererIncludePaths);
        options.writeSourceToDisk = !argSkipWritingSource;
        options.oslCompilerPath = new FilePath(argOslCompilerPath);

        // We'll use this boolean to return an error code is one of the `NodeDef` failed to codegen/compile.
        bool hasFailed = false;

        // We create and use a dedicated `NodeGraph` to avoid `NodeDef` names collision.
        NodeGraph librariesDocGraph = librariesDoc.addNodeGraph("librariesDocGraph");

        // Loop over all the `NodeDef` gathered in our documents from the provided libraries.
        foreach (NodeDef nodeDef in librariesDoc.getNodeDefs())
        {
            // Determine whether or not there's a valid implementation of the current `NodeDef` for the type associated
            // to our OSL shader generator, i.e. OSL, and if not, skip it.
            InterfaceElement nodeImpl = nodeDef.getImplementation(oslShaderGen.getTarget(), false);

            if (nodeImpl == null)
            {
                Console.Out.Write("The following `NodeDef` does not provide a valid OSL implementation, " +
                                  "and will be skipped: " + nodeDef.getName() + "\n");

                continue;
            }

            // Intention is here is to name the new node the same as the genosl implementation name
            // but replacing "_genosl" with "_genoslnetwork"
            string nodeName;
            if (argOsoNameStrategy == "implementation")
            {
                // Name the node the same as the implementation with _genoslnetwork added as a suffix.
                // NOTE : If the implementation currently has _genosl as a suffix then we remove it.
                nodeName = nodeImpl.getName();
                StringMap substitutions = new StringMap();
                substitutions["_genosl"] = "";
                nodeName = replaceSubstrings(nodeName, substitutions);
                nodeName += "_genoslnetwork";
            }
            else
            {
                // Name the node the same as the node definition
                nodeName = nodeDef.getName();
            }

            Node node = librariesDocGraph.addNodeInstance(nodeDef, nodeName);
            if (node == null)
            {
                Console.Error.Write("Unable to create Node instance for NodeDef - '" + nodeDef.getName() + "'" + "\n");
                return 1;
            }

            string oslShaderName = node.getName();
            oslShaderGen.getSyntax().makeValidName(ref oslShaderName);

            string oslFileName = oslShaderName + ".osl";
            string oslFilePath = (outputOsoPath / oslFileName).asString();

            // Codegen the `Node` to an `.osl` file.
            Shader oslShader = null;
            try
            {
                // Codegen the `Node` to OSL.
                oslShader = oslShaderGen.generate(node.getName(), node, context);

                // Dump the content of the codegen'd `NodeDef` to our `.osl` file.
                File.WriteAllText(oslFilePath, oslShader.getSourceCode(), new UTF8Encoding(false));
            }
            // Catch any codegen/compilation related exceptions.
            catch (ExceptionShaderGenError exc)
            {
                Console.Error.Write("Encountered a shader codegen related exception for the " +
                                    "following node: " + nodeDef.getName() + "\n");
                Console.Error.Write(exc.what() + "\n");

                hasFailed = true;
            }

            // Compile the codegen'd `.osl` file.
            try
            {
                // Compile the `.osl` file to a `.oso` file next to it.
                compileOSL(oslShader.getSourceCode(), new FilePath(oslFilePath), options);

                {
                    string implName = "IMPL_" + nodeName + "_" + target;
                    Implementation impl = implMtlxDoc.addImplementation(implName);
                    impl.setNodeDef(nodeDef);
                    // TODO: stash the the OSO path here.
                    // This is writing the absolute path - which we don't want
                    impl.setFile(argLibraryRelativeOsoPath);

                    impl.setFunction(oslShaderName);
                    impl.setAttribute("sourcecode", "dummy");
                    impl.setTarget(target);
                }
            }
            // Catch any codegen/compilation related exceptions.
            catch (ExceptionCompileError exc)
            {
                Console.Error.Write("Encountered a shader compilation related exception for the " +
                                    "following node: " + nodeDef.getName() + "\n");
                Console.Error.Write(exc.what() + "\n");

                // Dump details about the exception in the log file.
                foreach (string error in exc.errorLog())
                {
                    Console.Error.Write(error + "\n");
                }

                hasFailed = true;
            }
        }

        if (implMtlxDoc != null)
        {
            // We only write the MaterialX document containing the implementations out if requested.
            FilePath implMtlxDocFilePath = outputMtlxPath / "genoslnetwork_impl.mtlx";
            XmlWriteOptions writeOptions = new XmlWriteOptions();
            writeOptions.createDirectories = true;
            try
            {
                writeToXmlFile(implMtlxDoc, implMtlxDocFilePath, writeOptions);
            }
            // Catch any file writing related exceptions.
            catch (MaterialX.Exception exc)
            {
                Console.Error.Write("Encountered an exception while writing the implementation document to the " +
                                    "following path: " + implMtlxDocFilePath.asString() + "\n");
                Console.Error.Write(exc.what() + "\n");

                hasFailed = true;
            }
        }

        // If something went wrong, return an appropriate error code.
        if (hasFailed)
        {
            Console.Error.Write("Failed to codegen and compile all the OSL shaders associated to the provided MaterialX " +
                                "libraries, see the log file for more details." + "\n");

            return 1;
        }

        return 0;
    }
}
