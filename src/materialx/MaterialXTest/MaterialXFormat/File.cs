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
using static MaterialXTest.Catch;

public static class FormatFileTests
{
    [TEST("Format", "SyntacticOperations")]
    public static void Format_SyntacticOperations()
    {
        List<(string, FilePath.Format)> inputPairs = new List<(string, FilePath.Format)>
        {
            ("D:\\Assets\\Materials\\Robot.mtlx", FilePath.Format.FormatWindows),
            ("\\\\Show\\Assets\\Materials\\Robot.mtlx", FilePath.Format.FormatWindows),
            ("Materials\\Robot.mtlx", FilePath.Format.FormatWindows),
            ("/Assets/Materials/Robot.mtlx", FilePath.Format.FormatPosix),
            ("Assets/Materials/Robot.mtlx", FilePath.Format.FormatPosix),
            ("Materials/Robot.mtlx", FilePath.Format.FormatPosix)
        };

        foreach ((string, FilePath.Format) pair in inputPairs)
        {
            FilePath path = new FilePath(pair.Item1);
            REQUIRE(path.asString(pair.Item2) == pair.Item1);
        }
    }

    [TEST("Format", "FileSystemOperations")]
    public static void Format_FileSystemOperations()
    {
        FileSearchPath searchPath = getDefaultDataSearchPath();
        List<FilePath> examplePaths = new List<FilePath>
        {
            "libraries/stdlib/stdlib_defs.mtlx",
            "resources/Materials/Examples/StandardSurface/standard_surface_brass_tiled.mtlx",
            "resources/Materials/Examples/StandardSurface/standard_surface_marble_solid.mtlx",
        };
        foreach (FilePath path in examplePaths)
        {
            REQUIRE(searchPath.find(path).exists());
        }

        REQUIRE(FilePath.getCurrentPath().exists());
        REQUIRE(FilePath.getModulePath().exists());
    }

    [TEST("Format", "FileSearchPathOperations")]
    public static void Format_FileSearchPathOperations()
    {
        FileSearchPath searchPath = getDefaultDataSearchPath();
        searchPath.append(searchPath.find("libraries/stdlib"));
        searchPath.append(searchPath.find("resources/Materials/Examples/StandardSurface"));

        List<FilePath> filenames = new List<FilePath>
        {
            "stdlib_defs.mtlx",
            "standard_surface_brass_tiled.mtlx",
            "standard_surface_marble_solid.mtlx",
        };

        foreach (FilePath filename in filenames)
        {
            REQUIRE(searchPath.find(filename).exists());
        }
    }

    [TEST("Format", "FlattenFilenames")]
    public static void Format_FlattenFilenames()
    {
        FilePath TEST_FILE_PREFIX_STRING = new FilePath("resources\\Images\\");
        FilePath TEST_IMAGE_STRING1 = new FilePath("brass_roughness.jpg");
        FilePath TEST_IMAGE_STRING2 = new FilePath("brass_color.jpg");

        Document doc1 = createDocument();

        // Set up document
        NodeGraph nodeGraph = doc1.addNodeGraph();
        nodeGraph.setFilePrefix(TEST_FILE_PREFIX_STRING.asString() + "\\"); // Note this is required as filepath->string strips out last separator
        Node image1 = nodeGraph.addNode("image");
        image1.setInputValue("file", "brass_roughness.jpg", FILENAME_TYPE_STRING);
        Node image2 = nodeGraph.addNode("image");
        image2.setInputValue("file", "brass_color.jpg", FILENAME_TYPE_STRING);

        // 1. Test resolving fileprefix
        flattenFilenames(doc1);
        REQUIRE(nodeGraph.getFilePrefix() == EMPTY_STRING);
        FilePath resolvedPath = new FilePath(image1.getInputValue("file").getValueString());
        REQUIRE(resolvedPath.isEqual(TEST_FILE_PREFIX_STRING / TEST_IMAGE_STRING1));
        resolvedPath = new FilePath(image2.getInputValue("file").getValueString());
        REQUIRE(resolvedPath.isEqual(TEST_FILE_PREFIX_STRING / TEST_IMAGE_STRING2));

        // Reset document
        nodeGraph.setFilePrefix(TEST_FILE_PREFIX_STRING.asString() + "\\");
        image1.setInputValue("file", "brass_roughness.jpg", FILENAME_TYPE_STRING);
        image2.setInputValue("file", "brass_color.jpg", FILENAME_TYPE_STRING);

        // 2. Test resolving to absolute paths
        // (not in MaterialX) Upstream resolves against the images of resources/Images,
        // which are not carried in this repository, so a directory holding files of the
        // same names stands in for the root of the data search path.
        FilePath temporaryRoot = FilePath.createTemporaryDirectory();
        (temporaryRoot / "resources" / "Images").createDirectory(true);
        File.WriteAllText((temporaryRoot / "resources" / "Images" / TEST_IMAGE_STRING1).asString(), "content");
        File.WriteAllText((temporaryRoot / "resources" / "Images" / TEST_IMAGE_STRING2).asString(), "content");
        FileSearchPath searchPath = new FileSearchPath(temporaryRoot.asString());
        FilePath rootPath = searchPath.isEmpty() ? new FilePath() : searchPath[0];

        flattenFilenames(doc1, searchPath);    
        REQUIRE(nodeGraph.getFilePrefix() == EMPTY_STRING);
        resolvedPath = new FilePath(image1.getInputValue("file").getValueString());
        REQUIRE(resolvedPath.asString() == (rootPath / TEST_FILE_PREFIX_STRING / TEST_IMAGE_STRING1).asString());
        resolvedPath = new FilePath(image2.getInputValue("file").getValueString());
        REQUIRE(resolvedPath.asString() == (rootPath / TEST_FILE_PREFIX_STRING / TEST_IMAGE_STRING2).asString());

        // Reset document
        nodeGraph.setFilePrefix(TEST_FILE_PREFIX_STRING.asString() + "\\");
        image1.setInputValue("file", "brass_roughness.jpg", FILENAME_TYPE_STRING);
        image2.setInputValue("file", "brass_color.jpg", FILENAME_TYPE_STRING);

        // 3. Test with additional resolvers
        // - Create resolver to replace all Windows separators with POSIX ones
        StringResolver separatorReplacer = StringResolver.create();
        separatorReplacer.setFilenameSubstitution("\\\\", "/");
        separatorReplacer.setFilenameSubstitution("\\", "/");

        flattenFilenames(doc1, searchPath, separatorReplacer);
        REQUIRE(nodeGraph.getFilePrefix() == EMPTY_STRING);
        string resolvedPathString = image1.getInputValue("file").getValueString();
        REQUIRE(resolvedPathString == (rootPath / TEST_FILE_PREFIX_STRING / TEST_IMAGE_STRING1).asString(FilePath.Format.FormatPosix));
        resolvedPathString = image2.getInputValue("file").getValueString();
        REQUIRE(resolvedPathString == (rootPath / TEST_FILE_PREFIX_STRING / TEST_IMAGE_STRING2).asString(FilePath.Format.FormatPosix));

        // 4. Test with pre-resolved filenames
        nodeGraph.setFilePrefix(TEST_FILE_PREFIX_STRING.asString() + "\\");
        flattenFilenames(doc1, searchPath, separatorReplacer);
        REQUIRE(nodeGraph.getFilePrefix() == EMPTY_STRING);
        resolvedPathString = image1.getInputValue("file").getValueString();
        REQUIRE(resolvedPathString == (rootPath / TEST_FILE_PREFIX_STRING / TEST_IMAGE_STRING1).asString(FilePath.Format.FormatPosix));
        resolvedPathString = image2.getInputValue("file").getValueString();
        REQUIRE(resolvedPathString == (rootPath / TEST_FILE_PREFIX_STRING / TEST_IMAGE_STRING2).asString(FilePath.Format.FormatPosix));
        REQUIRE(temporaryRoot.removeDirectory(true));
    }

    [TEST("Format", "PathNormalizationTest")]
    public static void Format_PathNormalizationTest()
    {
        FilePath REFERENCE_REL_PATH = new FilePath("a/b");
        FilePath REFERENCE_ABS_PREFIX = new FilePath("/assets");

        List<FilePath> examplePaths = new List<FilePath>
        {
            "a/./b",
            "././a/b",
            "c/../d/../a/b",
            "a/b/./c/d/../.."
        };

        foreach (FilePath path in examplePaths)
        {
            REQUIRE(path.getNormalized().isEqual(REFERENCE_REL_PATH));
            REQUIRE((REFERENCE_ABS_PREFIX / path).getNormalized().isEqual(REFERENCE_ABS_PREFIX / REFERENCE_REL_PATH));
        }
    }

    [TEST("Format", "GetAllFilesInDirectory")]
    public static void Format_GetAllFilesInDirectory()
    {
        List<FilePath> mtlxFilenames = new List<FilePath>
        {
            "environment_map.mtlx",
            "goegap_split.mtlx",
            "san_giuseppe_bridge_split.mtlx",
            "table_mountain_split.mtlx"
        };

        List<FilePath> hdrFilenames = new List<FilePath>
        {
            "goegap.hdr",
            "goegap_split.hdr",
            "san_giuseppe_bridge.hdr",
            "san_giuseppe_bridge_split.hdr",
            "table_mountain.hdr",
            "table_mountain_split.hdr",
        };

        List<FilePath> allFilesnames = new List<FilePath>(hdrFilenames);
        allFilesnames.InsertRange(0, mtlxFilenames);

        // (not in MaterialX) The test reads resources/Lights upstream. The
        // environment maps of that directory are not carried in this
        // repository, so a directory with the same file names stands in.
        FilePath lightsDir = FilePath.createTemporaryDirectory();
        foreach (FilePath filename in allFilesnames)
        {
            File.WriteAllText((lightsDir / filename).asString(), "content");
        }

        List<FilePath> results;

        results = lightsDir.getFilesInDirectory("mtlx");
        REQUIRE(results.Count > 0);
        foreach (FilePath filename in mtlxFilenames)
        {
            REQUIRE(results.Exists((FilePath result) => result.isEqual(filename)));
        }

        results = lightsDir.getFilesInDirectory("hdr");
        REQUIRE(results.Count > 0);
        foreach (FilePath filename in hdrFilenames)
        {
            REQUIRE(results.Exists((FilePath result) => result.isEqual(filename)));
        }

        results = lightsDir.getFilesInDirectory();
        REQUIRE(results.Count > 0);
        foreach (FilePath filename in allFilesnames)
        {
            REQUIRE(results.Exists((FilePath result) => result.isEqual(filename)));
        }

        REQUIRE(lightsDir.removeDirectory(true));
    }

    [TEST("Format", "SystemTemporaryDirectory")]
    public static void Format_SystemTemporaryDirectory()
    {
        FilePath systemTempDir = FilePath.getSystemTemporaryDirectory();

        REQUIRE(!systemTempDir.isEmpty());
        REQUIRE(systemTempDir.exists());
        REQUIRE(systemTempDir.isDirectory());
    }

    [TEST("Format", "CreateTemporaryDirectory")]
    public static void Format_CreateTemporaryDirectory()
    {
        // Create a temporary directory within the system temporary directory.
        FilePath systemTempDir = FilePath.getSystemTemporaryDirectory();
        FilePath tempDir = FilePath.createTemporaryDirectory();
        REQUIRE(tempDir.exists());
        REQUIRE(tempDir.isDirectory());
        REQUIRE(tempDir.getParentPath().getNormalized().isEqual(systemTempDir.getNormalized()));

        // Verify that each temporary directory is unique.
        FilePath secondTempDir = FilePath.createTemporaryDirectory();
        REQUIRE(secondTempDir.isDirectory());
        REQUIRE(!secondTempDir.isEqual(tempDir));

        // Create a temporary directory within an explicit parent directory, which
        // is itself created on demand.
        FilePath explicitParent = tempDir / "parent" / "subdirectory";
        REQUIRE(!explicitParent.exists());
        FilePath childTempDir = FilePath.createTemporaryDirectory(explicitParent);
        REQUIRE(childTempDir.isDirectory());
        REQUIRE(childTempDir.getParentPath().getNormalized().isEqual(explicitParent.getNormalized()));

        // Verify that a parent path referring to an existing file is rejected.
        FilePath existingFile = tempDir / "file.txt";
        File.WriteAllText(existingFile.asString(), "content");
        REQUIRE(existingFile.exists());
        REQUIRE(!existingFile.isDirectory());
        REQUIRE_THROWS_AS<MaterialX.Exception>(() => { FilePath.createTemporaryDirectory(existingFile); });

        if (!OperatingSystem.IsWindows())
        {
            // Verify that the temporary directory is accessible only to the current user.
            UnixFileMode userOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
            REQUIRE(File.GetUnixFileMode(tempDir.asString()) == userOnly);

            // Verify that a failure to create the directory is reported as an exception,
            // rather than returning a path that does not exist.  The root user bypasses
            // permission checks, so this case is only meaningful for other users.
            if (!Environment.IsPrivilegedProcess)
            {
                FilePath readOnlyParent = tempDir / "readOnly";
                readOnlyParent.createDirectory();
                REQUIRE(readOnlyParent.isDirectory());
                File.SetUnixFileMode(readOnlyParent.asString(), UnixFileMode.UserRead | UnixFileMode.UserExecute);
                REQUIRE_THROWS_AS<MaterialX.Exception>(() => { FilePath.createTemporaryDirectory(readOnlyParent); });
                File.SetUnixFileMode(readOnlyParent.asString(), userOnly);
            }
        }

        REQUIRE(tempDir.removeDirectory(true));
        REQUIRE(!tempDir.exists());
        REQUIRE(secondTempDir.removeDirectory(true));
        REQUIRE(!secondTempDir.exists());
    }

    [TEST("Format", "RemoveDirectory")]
    public static void Format_RemoveDirectory()
    {
        FilePath tempDir = FilePath.createTemporaryDirectory();

        // Verify that a non-recursive removal leaves a non-empty directory in place.
        FilePath nestedDir = tempDir / "nested" / "subdirectory";
        nestedDir.createDirectory(true);
        File.WriteAllText((nestedDir / "file.txt").asString(), "content");
        REQUIRE(!tempDir.removeDirectory());
        REQUIRE(tempDir.isDirectory());

        // Verify that a non-recursive removal succeeds for an empty directory.
        FilePath emptyDir = tempDir / "empty";
        emptyDir.createDirectory();
        REQUIRE(emptyDir.removeDirectory());
        REQUIRE(!emptyDir.exists());

        // Verify that a removal of a path that does not exist is reported as a failure.
        REQUIRE(!emptyDir.removeDirectory());
        REQUIRE(!emptyDir.removeDirectory(true));

        // Verify that a removal of an empty path is reported as a failure.
        REQUIRE(!new FilePath().removeDirectory());
        REQUIRE(!new FilePath().removeDirectory(true));

        FilePath linkTarget = null;
        if (!OperatingSystem.IsWindows())
        {
            // Verify that a symbolic link within the directory is removed without following
            // it to its target.
            linkTarget = FilePath.createTemporaryDirectory();
            File.WriteAllText((linkTarget / "preserved.txt").asString(), "content");
            FilePath link = nestedDir / "link";
            Directory.CreateSymbolicLink(link.asString(), linkTarget.asString());
            REQUIRE(link.isDirectory());
        }

        // Verify that a recursive removal clears the directory and its contents.
        REQUIRE(tempDir.removeDirectory(true));
        REQUIRE(!tempDir.exists());

        if (!OperatingSystem.IsWindows())
        {
            REQUIRE(linkTarget.isDirectory());
            REQUIRE((linkTarget / "preserved.txt").exists());
            REQUIRE(linkTarget.removeDirectory(true));
        }
    }
}
