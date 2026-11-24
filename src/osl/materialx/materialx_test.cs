// (not in OpenShadingLanguage) materialx_test.cs

using System.Collections.Generic;
using System.IO;
using OIIO;
using testing;
using static testing.Gtest;

namespace OSL
{
    public static class MaterialXTranslatorTests
    {
        [TEST("MaterialX", "DocumentsBecomeShaders")]
        public static void MaterialX_DocumentsBecomeShaders()
        {
            // every example document of MaterialX is written as an OSL surface shader by the port in src/materialx, and compiles
            string library = MaterialXTranslator.DefaultLibraryDirectory();
            string[] documents = Directory.GetFiles(Path.Combine(library, "resources", "Materials", "Examples"), "*.mtlx", SearchOption.AllDirectories);
            EXPECT_TRUE(documents.Length >= 50);
            foreach (string document in documents)
            {
                string source, name, error;
                Dictionary<string, string> parameters;
                bool translated = MaterialXTranslator.Translate(document, "", library, out source, out name, out parameters, out error);
                if (!translated)
                {
                    System.Console.WriteLine(Path.GetFileName(document) + ": " + error);
                }
                EXPECT_TRUE(translated);
                if (!translated)
                {
                    continue;
                }
                EXPECT_TRUE(name.StartsWith("mtlx_"));
                EXPECT_TRUE(source.Contains("surface " + name));
                EXPECT_TRUE(source.Contains("Ci = out;"));
                BufferedErrorHandler errors = new BufferedErrorHandler();
                OSLCompiler compiler = new OSLCompiler(errors);
                string oso;
                bool compiled = compiler.compile_buffer(source, out oso, new List<string>(), Path.Combine(System.AppContext.BaseDirectory, "shaders", "stdosl.h"),
                                                        name + ".osl");
                if (!compiled)
                {
                    System.Console.WriteLine(Path.GetFileName(document) + ": " + string.Join("\n", errors.errors));
                }
                EXPECT_TRUE(compiled);
                if (compiled)
                {
                    EXPECT_TRUE(oso.Contains("closure"));
                }
            }
        }

        [TEST("MaterialX", "ShortParameterNames")]
        public static void MaterialX_ShortParameterNames()
        {
            string library = MaterialXTranslator.DefaultLibraryDirectory();
            string document = Path.Combine(library, "resources", "Materials", "Examples", "StandardSurface", "standard_surface_carpaint.mtlx");
            string source, name, error;
            Dictionary<string, string> parameters;
            ASSERT_TRUE(MaterialXTranslator.Translate(document, "", library, out source, out name, out parameters, out error));
            // the inputs of the surface shader node, by their own names
            ASSERT_TRUE(parameters.ContainsKey("base_color"));
            EXPECT_TRUE(parameters["base_color"].EndsWith("_base_color"));
            EXPECT_TRUE(source.Contains(parameters["base_color"]));

            // a material that the document does not have
            EXPECT_FALSE(MaterialXTranslator.Translate(document, "No_Such_Material", library, out source, out name, out parameters, out error));
            EXPECT_TRUE(error.Contains("No_Such_Material"));
        }
    }
}
