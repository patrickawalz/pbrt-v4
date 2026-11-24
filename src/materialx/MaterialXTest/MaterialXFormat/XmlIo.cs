//
// Copyright Contributors to the MaterialX Project
// SPDX-License-Identifier: Apache-2.0
//

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using MaterialX;
using testing;
using static MaterialX.Mx;
using MaterialXTest;
using static MaterialXTest.Catch;

public static class FormatXmlIoTests
{
    [TEST("Format", "LoadContent")]
    public static void Format_LoadContent()
    {
        FileSearchPath searchPath = getDefaultDataSearchPath();
        FilePath libraryPath = searchPath.find("libraries/stdlib");
        FilePath examplesPath = searchPath.find("resources/Materials/Examples/StandardSurface");
        searchPath.append(libraryPath);
        searchPath.append(examplesPath);

        // Read the standard library.
        List<Document> libs = new List<Document>();
        foreach (FilePath filename in libraryPath.getFilesInDirectory(MTLX_EXTENSION))
        {
            Document lib = createDocument();
            readFromXmlFile(lib, filename, searchPath);
            libs.Add(lib);
        }

        // Read and validate each example document.
        foreach (FilePath filename in examplesPath.getFilesInDirectory(MTLX_EXTENSION))
        {
            Document doc = createDocument();
            readFromXmlFile(doc, filename, searchPath);
            foreach (Document lib in libs)
            {
                doc.importLibrary(lib);
            }
            StringBuilder message = new StringBuilder();
            bool docValid = doc.validate(message);
            if (!docValid)
            {
                WARN("[" + filename.asString() + "] " + message.ToString());
            }
            REQUIRE(docValid);

            // Traverse the document tree
            int valueElementCount = 0;
            foreach (Element elem in doc.traverseTree())
            {
                if (elem.isA<ValueElement>())
                {
                    valueElementCount++;
                }
            }
            REQUIRE(valueElementCount > 0);

            // Serialize to XML.
            XmlWriteOptions writeOptions = new XmlWriteOptions();
            writeOptions.writeXIncludeEnable = false;
            string xmlString = writeToXmlString(doc, writeOptions);

            // Verify that the serialized document is identical.
            Document writtenDoc = createDocument();
            readFromXmlString(writtenDoc, xmlString);
            REQUIRE(writtenDoc.isEqual(doc));
            readFromXmlBuffer(writtenDoc, xmlString);
            REQUIRE(writtenDoc.isEqual(doc));

            // Flatten all subgraphs.
            doc.flattenSubgraphs();
            foreach (NodeGraph graph in doc.getNodeGraphs())
            {
                if (graph.belongsToContentDocument())
                {
                    graph.flattenSubgraphs();
                }
            }
            REQUIRE(doc.validate());

            // Verify that all referenced types and nodes are declared.
            bool referencesValid = true;
            foreach (Element elem in doc.traverseTree())
            {
                if (!elem.belongsToContentDocument())
                {
                    continue;
                }

                TypedElement typedElem = elem.asA<TypedElement>();
                if (typedElem != null && typedElem.hasType() && !typedElem.isMultiOutputType())
                {
                    if (typedElem.getTypeDef() == null)
                    {
                        WARN("[" + typedElem.getActiveSourceUri() + "] TypedElement " + typedElem.getName() + " has no matching TypeDef");
                        referencesValid = false;
                    }
                }
                Node node = elem.asA<Node>();
                if (node != null)
                {
                    if (node.getNodeDefString().Length != 0 && node.getNodeDef() == null)
                    {
                        WARN("[" + node.getActiveSourceUri() + "] Node " + node.getName() + " has no matching NodeDef for " + node.getNodeDefString());
                        referencesValid = false;
                    }
                }
            }
            REQUIRE(referencesValid);
        }

        // Read the same document twice and verify that duplicate elements
        // are skipped.
        Document doc2 = createDocument();
        string filename2 = "standard_surface_look_brass_tiled.mtlx";
        readFromXmlFile(doc2, filename2, searchPath);
        readFromXmlFile(doc2, filename2, searchPath);
        REQUIRE(doc2.validate());

        // Import libraries twice and verify that duplicate elements are
        // skipped.
        Document libDoc = doc2.copy();
        foreach (Document lib in libs)
        {
            libDoc.importLibrary(lib);
            libDoc.importLibrary(lib);
        }
        REQUIRE(libDoc.validate());

        // Read document with conflicting elements.
        Document conflictDoc = doc2.copy();
        foreach (Element elem in conflictDoc.traverseTree())
        {
            if (elem.isA<Node>("image"))
            {
                elem.setFilePrefix("differentFolder/");
            }
        }
        readFromXmlFile(conflictDoc, filename2, searchPath);
        REQUIRE(conflictDoc.validate());

        // Reread in clean document
        doc2 = createDocument();
        readFromXmlFile(doc2, filename2, searchPath);

        // Read document without XIncludes.
        Document flatDoc = createDocument();
        XmlReadOptions readOptions = new XmlReadOptions();
        readOptions.readXIncludeFunction = null;
        readFromXmlFile(flatDoc, filename2, searchPath, readOptions);
        readOptions.readXIncludeFunction = readFromXmlFile;
        REQUIRE(!flatDoc.isEqual(doc2));

        // Read document using environment search path.
        setEnviron(MATERIALX_SEARCH_PATH_ENV_VAR, searchPath.asString());
        Document envDoc = createDocument();
        readFromXmlFile(envDoc, filename2, new FileSearchPath(), readOptions);
        REQUIRE(doc2.isEqual(envDoc));
        removeEnviron(MATERIALX_SEARCH_PATH_ENV_VAR);
        REQUIRE_THROWS_AS<ExceptionFileMissing>(() => { readFromXmlFile(envDoc, filename2, new FileSearchPath(), readOptions); });

        // Serialize to XML with a custom predicate that skips images.
        Func<Element, bool> skipImages = (Element elem) =>
        {
            return !elem.isA<Node>("image");
        };
        XmlWriteOptions writeOptions2 = new XmlWriteOptions();
        writeOptions2.writeXIncludeEnable = false;
        writeOptions2.elementPredicate = skipImages;
        string xmlString2 = writeToXmlString(doc2, writeOptions2);

        // Reconstruct and verify that the document contains no images.
        Document writtenDoc2 = createDocument();
        readFromXmlString(writtenDoc2, xmlString2, new FileSearchPath(), readOptions);
        REQUIRE(!writtenDoc2.isEqual(doc2));
        uint imageElementCount = 0;
        foreach (Element elem in writtenDoc2.traverseTree())
        {
            if (elem.isA<Node>("image"))
            {
                imageElementCount++;
            }
        }
        REQUIRE(imageElementCount == 0);

        // Serialize to XML with a custom predicate to remove XIncludes.
        Func<Element, bool> skipLibIncludes = (Element elem) =>
        {
            if (elem.hasSourceUri())
            {
                foreach (Document lib in libs)
                {
                    if (lib.getSourceUri() == elem.getSourceUri())
                    {
                        return false;
                    }
                }
            }
            return true;
        };
        writeOptions2.writeXIncludeEnable = true;
        writeOptions2.elementPredicate = skipLibIncludes;
        xmlString2 = writeToXmlString(writtenDoc2, writeOptions2);

        // Verify that the document contains no XIncludes.
        writtenDoc2 = createDocument();
        readFromXmlString(writtenDoc2, xmlString2, new FileSearchPath(), readOptions);
        bool hasSourceUri = false;
        foreach (Element elem in writtenDoc2.traverseTree())
        {
            if (elem.hasSourceUri())
            {
                hasSourceUri = true;
                break;
            }
        }
        REQUIRE(!hasSourceUri);

        // Read a document with a relative XInclude.
        Document parentDoc = createDocument();
        readFromXmlFile(parentDoc, "resources/Materials/TestSuite/libraries/metal/brass_wire_mesh.mtlx", searchPath);
        REQUIRE(parentDoc.getNodeDef("ND_TestMetal") != null);

        // Read a string with a relative Xinclude
        parentDoc = createDocument();
        string includeTest =
            "<?xml version=\"1.0\"?> " +
            "<materialx version=\"1.38\">" +
            "<xi:include href=\"standard_surface_brass_tiled.mtlx\" />" +
            "<xi:include href=\"standard_surface_greysphere_calibration.mtlx\" />" +
            "</materialx>";
        readFromXmlString(parentDoc, includeTest, searchPath);
        REQUIRE(parentDoc.getNodeGraph("NG_brass1") != null);
        REQUIRE(parentDoc.getNodeGraph("NG_Greysphere_Calibration") != null);

        // Read a non-existent document.
        Document nonExistentDoc = createDocument();
        REQUIRE_THROWS_AS<ExceptionFileMissing>(() => { readFromXmlFile(nonExistentDoc, "NonExistent.mtlx", new FileSearchPath(), readOptions); });
    }

    [TEST("Format", "CommentsAndNewlines")]
    public static void Format_CommentsAndNewlines()
    {
        FileSearchPath searchPath = getDefaultDataSearchPath();
        FilePath testPath = searchPath.find("resources/Materials/Examples/StandardSurface/standard_surface_chess_set.mtlx");

        // Read the example file into an XML string buffer.
        string origXml = readFile(testPath);

        // Convert the string to a document with comments and newlines preserved.
        Document doc = createDocument();
        XmlReadOptions readOptions = new XmlReadOptions();
        readOptions.readComments = true;
        readOptions.readNewlines = true;
        readOptions.upgradeVersion = false;
        readFromXmlString(doc, origXml, new FileSearchPath(), readOptions);

        // Write the document to a new XML string buffer.
        string newXml = writeToXmlString(doc);

        // Verify that the XML string buffers are identical.
        REQUIRE(origXml == newXml);
    }

    [TEST("Format", "MaximumTreeDepth")]
    public static void Format_MaximumTreeDepth()
    {
        // Create a document that exceeds the maximum tree depth.
        Document doc = createDocument();
        Element elem = doc;
        for (int i = 0; i < MAX_XML_TREE_DEPTH + 1; i++)
        {
            elem = elem.addChild<NodeGraph>();
        }

        // Write the document to a string buffer.
        string xmlString = writeToXmlString(doc);

        // Read the string buffer as a document, verifying that the correct
        // exception is thrown.
        Document newDoc = createDocument();
        REQUIRE_THROWS_AS<ExceptionParseError>(() => { readFromXmlString(newDoc, xmlString); });
    }

    [TEST("Format", "FuzzTesting")]
    public static void Format_FuzzTesting()
    {
        FileSearchPath searchPath = getDefaultDataSearchPath();
        FilePath examplesPath = searchPath.find("resources/Materials/Examples");

        mt19937 rng = new mt19937(0);

        foreach (FilePath path in examplesPath.getSubDirectories())
        {
            foreach (FilePath filename in path.getFilesInDirectory(MTLX_EXTENSION))
            {
                // Read the example file into an XML string buffer.
                byte[] origString = File.ReadAllBytes((path / filename).asString());
                REQUIRE(origString.Length > 0);

                // Iterate over test runs.
                for (int testRun = 0; testRun < 256; testRun++)
                {
                    byte[] editString = (byte[])origString.Clone();

                    // Iterate over string edits.
                    for (int editIndex = 0; editIndex < 32; editIndex++)
                    {
                        // Randomly alter one character in the document.
                        int charIndex = StdRandom.uniformInt(rng, 0, origString.Length - 1);
                        int newChar = StdRandom.uniformInt(rng, 0, 255);
                        editString[charIndex] = (byte)newChar;

                        // Attempt to interpret the edited string as a document, allowing only MaterialX exceptions.
                        Document doc = createDocument();
                        try
                        {
                            using (MemoryStream stream = new MemoryStream(editString))
                            {
                                readFromXmlStream(doc, stream, searchPath);
                            }
                            doc.validate();
                        }
                        catch (MaterialX.Exception)
                        {
                            // On a MaterialX exception, proceed to the next test run.
                            break;
                        }
                    }
                }
            }
        }
    }

    [TEST("Format", "LocaleRegionTesting")]
    public static void Format_LocaleRegionTesting()
    {
        // In the United States, the thousands separator is a comma, while in Germany it is a period.
        // Thus, one thousand twenty five is displayed as 1,025 in the United States and 1.025 in Germany.
        //
        // In a MaterialX document, a vector3 value of "1,1.5,2.0" should be interpreted as (1.0f, 1.5f, 2.0f).

        // Set the locale to Germany, if supported on this platform, and note the original locale.
        // (not in MaterialX) The test host runs with invariant globalization, so
        // the German conventions for numbers are set on a copy of the invariant culture.
        CultureInfo origLocale = CultureInfo.CurrentCulture;
        CultureInfo german = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        german.NumberFormat.NumberDecimalSeparator = ",";
        german.NumberFormat.NumberGroupSeparator = ".";
        CultureInfo.CurrentCulture = german;

        // Read and validate each example document.
        FilePath testPath = new FilePath("resources/Materials/TestSuite/locale");
        foreach (FilePath filename in testPath.getFilesInDirectory(MTLX_EXTENSION))
        {
            Document doc = createDocument();
            readFromXmlFile(doc, testPath / filename);
            REQUIRE(doc.validate());

            foreach (Element elem in doc.traverseTree())
            {
                if (elem.isA<ValueElement>())
                {
                    if (elem.hasAttribute("uiname"))
                    {
                        REQUIRE(elem.getAttribute("uiname").Length != 0);
                    }
                }
            }
        }

        // Restore the original locale.
        CultureInfo.CurrentCulture = origLocale;
    }

    [TEST("Format", "WriteWithCreatedDirectories")]
    public static void Format_WriteWithCreatedDirectories()
    {
        Document doc = createDocument();
        NodeGraph nodeGraph = doc.addNodeGraph("nodegraph1");
        nodeGraph.addNode("image", "image1");

        FilePath tempDir = FilePath.createTemporaryDirectory();
        FilePath filename = tempDir / "new" / "nested" / "directory" / "document.mtlx";
        REQUIRE(!filename.getParentPath().exists());

        // By default, no directories are created, and the write fails.
        XmlWriteOptions writeOptions = new XmlWriteOptions();
        REQUIRE_THROWS_AS<ExceptionFileMissing>(() => { writeToXmlFile(doc, filename, writeOptions); });
        REQUIRE(!filename.exists());

        // With the createDirectories option, the parent hierarchy is created.
        writeOptions.createDirectories = true;
        writeToXmlFile(doc, filename, writeOptions);
        REQUIRE(filename.getParentPath().isDirectory());
        REQUIRE(filename.exists());

        // Verify that the written document may be read back without loss.
        Document writtenDoc = createDocument();
        readFromXmlFile(writtenDoc, filename);
        REQUIRE(writtenDoc.isEqual(doc));

        // Verify that writing to an existing directory remains supported.
        FilePath secondFilename = filename.getParentPath() / "document2.mtlx";
        writeToXmlFile(doc, secondFilename, writeOptions);
        REQUIRE(secondFilename.exists());

        // Verify that a permission failure is reported as an exception, rather than
        // silently writing nothing.  The root user bypasses permission checks, so
        // this case is only meaningful for other users.
        if (!OperatingSystem.IsWindows() && !Environment.IsPrivilegedProcess)
        {
            FilePath readOnlyDir = tempDir / "readOnly";
            readOnlyDir.createDirectory();
            File.SetUnixFileMode(readOnlyDir.asString(), UnixFileMode.UserRead | UnixFileMode.UserExecute);
            FilePath deniedFilename = readOnlyDir / "denied" / "document.mtlx";
            REQUIRE_THROWS_AS<ExceptionFileMissing>(() => { writeToXmlFile(doc, deniedFilename, writeOptions); });
            REQUIRE(!deniedFilename.exists());
            File.SetUnixFileMode(readOnlyDir.asString(), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        REQUIRE(tempDir.removeDirectory(true));
        REQUIRE(!tempDir.exists());
    }
}
