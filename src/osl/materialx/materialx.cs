// (not in OpenShadingLanguage) materialx.cs: a MaterialX document as an Open Shading Language shader.
//
// MaterialX describes a material as a graph of nodes, and its library writes Open Shading Language source for such a graph with the OSL
// shader generator. src/materialx is a port of that library. This file asks the port for the shader of one material of a document and adds
// what Magi needs to run it:
//
//   the name of the shader      mtlx_<material>, so that it cannot collide with a shader of the scene
//   Ci                          the generated shader returns its closure in the output "out"; a surface shader of a renderer sets Ci
//   #include "mx_funcs.h"       is written with its full path, so that the source compiles from memory
//   file names                  are resolved against the document, so that its images are found
//   parameters                  the generator names a parameter <node>_<input>; the inputs of the surface shader node of the material
//                               are known by their own names as well ("base_color"), and Translate returns that table
//
// The data libraries are in src/materialx/libraries (Apache License 2.0, see the LICENSE there) and ship beside the program.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OSL
{
    // MaterialXTranslator Definition
    public sealed class MaterialXTranslator
    {
        // MaterialXTranslator Public Methods
        /// The OSL source of a material of a MaterialX document: the one named, or the first. libraryDirectory holds the directory "libraries".
        public static bool Translate(string documentPath, string material, string libraryDirectory, out string source, out string shaderName,
                                     out string error)
        {
            Dictionary<string, string> parameters;
            return Translate(documentPath, material, libraryDirectory, out source, out shaderName, out parameters, out error);
        }

        /// The same, with the parameters of the shader that have a short name: the inputs of the surface shader node of the material
        public static bool Translate(string documentPath, string material, string libraryDirectory, out string source, out string shaderName,
                                     out Dictionary<string, string> parameters, out string error)
        {
            parameters = new Dictionary<string, string>();
            source = "";
            shaderName = "";
            error = "";
            try
            {
                lock (mutex)
                {
                    MaterialX.FileSearchPath searchPath = new MaterialX.FileSearchPath(libraryDirectory);
                    MaterialX.Document libraries = Libraries(libraryDirectory, searchPath);

                    // Read the document; what it includes is looked for beside it and in the libraries
                    string fullPath = Path.GetFullPath(documentPath);
                    MaterialX.FileSearchPath documentSearchPath = new MaterialX.FileSearchPath(searchPath);
                    documentSearchPath.append(new MaterialX.FilePath(Path.GetDirectoryName(fullPath)));
                    MaterialX.Document document = MaterialX.Mx.createDocument();
                    MaterialX.Mx.readFromXmlFile(document, new MaterialX.FilePath(fullPath), documentSearchPath);
                    document.setDataLibrary(libraries);

                    // The images of the document are named relative to it
                    MaterialX.FileSearchPath imageSearchPath = new MaterialX.FileSearchPath(Path.GetDirectoryName(fullPath));
                    MaterialX.Mx.flattenFilenames(document, imageSearchPath);

                    // The material that is asked for, or the first
                    MaterialX.TypedElement element = null;
                    List<MaterialX.TypedElement> materials = MaterialX.Mx.findRenderableMaterialNodes(document);
                    foreach (MaterialX.TypedElement candidate in materials)
                    {
                        if (string.IsNullOrEmpty(material) || candidate.getName() == material)
                        {
                            element = candidate;
                            break;
                        }
                    }
                    if (element == null)
                    {
                        error = string.IsNullOrEmpty(material) ? "the MaterialX document has no surface material"
                                                               : "the MaterialX document has no material \"" + material + "\"";
                        return false;
                    }

                    // Generate the shader as the MaterialX library does for a renderer: with its colour management and its units
                    MaterialX.ShaderGenerator generator = MaterialX.OslShaderGenerator.create();
                    MaterialX.GenContext context = new MaterialX.GenContext(generator);
                    context.registerSourceCodeSearchPath(searchPath);
                    MaterialX.DefaultColorManagementSystem colorManagement = MaterialX.DefaultColorManagementSystem.create(generator.getTarget());
                    colorManagement.loadLibrary(libraries);
                    generator.setColorManagementSystem(colorManagement);
                    MaterialX.UnitSystem units = MaterialX.UnitSystem.create(generator.getTarget());
                    units.loadLibrary(libraries);
                    units.setUnitConverterRegistry(MaterialX.UnitConverterRegistry.create());
                    MaterialX.UnitTypeDef distance = libraries.getUnitTypeDef("distance");
                    units.getUnitConverterRegistry().addUnitConverter(distance, MaterialX.LinearUnitConverter.create(distance));
                    MaterialX.UnitTypeDef angle = libraries.getUnitTypeDef("angle");
                    units.getUnitConverterRegistry().addUnitConverter(angle, MaterialX.LinearUnitConverter.create(angle));
                    generator.setUnitSystem(units);
                    context.getOptions().targetDistanceUnit = "meter";
                    context.getOptions().targetColorSpaceOverride = "lin_rec709";
                    generator.registerTypeDefs(document);

                    shaderName = "mtlx_" + MaterialX.Mx.createValidName(element.getName());
                    MaterialX.Shader shader = generator.generate(shaderName, element, context);
                    source = ForRenderer(shader.getSourceCode(), shaderName, searchPath);

                    // The parameters of the surface shader node, by the names of its inputs
                    MaterialX.Node materialNode = element.asA<MaterialX.Node>();
                    if (materialNode != null)
                    {
                        foreach (MaterialX.Node shaderNode in MaterialX.Mx.getShaderNodes(materialNode))
                        {
                            string prefix = shaderNode.getName() + "_";
                            foreach (MaterialX.ShaderOutput socket in shader.getGraph().getInputSockets())
                            {
                                string name = socket.getName();
                                if (name.StartsWith(prefix, StringComparison.Ordinal) && !parameters.ContainsKey(name.Substring(prefix.Length)))
                                {
                                    parameters[name.Substring(prefix.Length)] = socket.getVariable();
                                }
                            }
                        }
                    }
                    return true;
                }
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        /// Where the libraries are when they ship beside the program: the directory that holds "libraries" and "resources"
        public static string DefaultLibraryDirectory()
        {
            return Path.Combine(AppContext.BaseDirectory, "materialx");
        }

        // MaterialXTranslator Private Methods
        /// The data libraries of a directory, loaded once
        private static MaterialX.Document Libraries(string libraryDirectory, MaterialX.FileSearchPath searchPath)
        {
            MaterialX.Document libraries;
            if (!loadedLibraries.TryGetValue(libraryDirectory, out libraries))
            {
                if (!Directory.Exists(Path.Combine(libraryDirectory, "libraries")))
                {
                    throw new InvalidOperationException("the MaterialX libraries were not found in \"" + libraryDirectory + "\"");
                }
                libraries = MaterialX.Mx.createDocument();
                List<MaterialX.FilePath> folders = new List<MaterialX.FilePath>();
                folders.Add(new MaterialX.FilePath("libraries"));
                MaterialX.Mx.loadLibraries(folders, searchPath, libraries);
                loadedLibraries[libraryDirectory] = libraries;
            }
            return libraries;
        }

        /// The generated source as a surface shader that compiles from memory
        private static string ForRenderer(string generated, string shaderName, MaterialX.FileSearchPath searchPath)
        {
            // The include of the MaterialX functions, with its full path
            const string include = "#include \"mx_funcs.h\"";
            int at = generated.IndexOf(include, StringComparison.Ordinal);
            if (at < 0)
            {
                throw new InvalidOperationException("the generated shader does not include mx_funcs.h");
            }
            string header = searchPath.find(new MaterialX.FilePath("libraries/stdlib/genosl/include/mx_funcs.h")).asString().Replace("\\", "/");
            StringBuilder source = new StringBuilder();
            source.Append(generated, 0, at).Append("#include \"").Append(header).Append("\"").Append(generated, at + include.Length,
                                                                                                generated.Length - at - include.Length);

            // The declaration of the shader: "shader <name>" becomes "surface <name>"
            string declaration = "\nshader " + shaderName + "\n";
            string text = source.ToString();
            int declared = text.LastIndexOf(declaration, StringComparison.Ordinal);
            if (declared < 0)
            {
                throw new InvalidOperationException("the generated shader has no declaration of \"" + shaderName + "\"");
            }
            text = text.Substring(0, declared) + "\nsurface " + shaderName + "\n" + text.Substring(declared + declaration.Length);

            // The closure of the material is the last assignment to the output "out"; the surface shader hands it to the renderer
            int end = text.LastIndexOf('}');
            if (end < 0)
            {
                throw new InvalidOperationException("the generated shader has no body");
            }
            return text.Substring(0, end) + "    Ci = out;\n" + text.Substring(end);
        }

        // MaterialXTranslator Private Members
        private static readonly object mutex = new object();
        private static readonly Dictionary<string, MaterialX.Document> loadedLibraries = new Dictionary<string, MaterialX.Document>();
    }
}
